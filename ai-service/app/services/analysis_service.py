"""Analysis service: the structured-output pipeline (ADR-0007) with retrieval (Phase 3).

Flow: change request -> hybrid retrieval (auto) -> evidence package -> layered prompt
-> provider -> Pydantic validation -> deterministic post-checks (grounding rule) ->
SUCCESS; else bounded repair, then SAFE FAILURE (422 AI_VALIDATION_FAILED). Unvalidated
prose is never returned as a result.
"""

from __future__ import annotations

import asyncio
import logging
import re
import time

from pydantic import BaseModel, ValidationError

from ..config import Settings
from ..errors import AiProviderError, AiRateLimitedError, AiTimeoutError, AiValidationError
from ..llm.prompts import (
    PromptBundle,
    build_evidence_index,
    build_incident_evidence_index,
    build_incident_prompt,
    build_repair_prompt,
    build_risk_prompt,
)
from ..models.requests import (
    DependencyRetrieval,
    IncidentAnalysisRequest,
    RetrievedDocumentItem,
    RiskAnalysisRequest,
)
from ..models.responses import (
    AnalysisUsage,
    IncidentAnalysisResponse,
    IncidentAnalysisResult,
    IncidentTurnResult,
    RetrievalTrace,
    RetrievalTraceItem,
    RiskAnalysisResponse,
    RiskAnalysisResult,
)
from ..providers.base import (
    IAIProvider,
    ProviderRateLimited,
    ProviderTimeout,
    ProviderUnavailable,
    StructuredResult,
)
from ..retrieval.service import RetrievalService

logger = logging.getLogger(__name__)


class AnalysisService:
    """Orchestrates retrieval + one structured reasoning call per request."""

    def __init__(
        self,
        *,
        provider: IAIProvider,
        settings: Settings,
        retrieval: RetrievalService | None = None,
    ):
        self._provider = provider
        self._settings = settings
        self._retrieval = retrieval

    @property
    def provider(self) -> IAIProvider:
        return self._provider

    async def analyze_change_risk(self, request: RiskAnalysisRequest) -> RiskAnalysisResponse:
        started = time.perf_counter()
        logger.info("analysis_started", extra={"projectId": request.project_id})

        request, trace = await self._maybe_retrieve(request)

        evidence_ids = set(build_evidence_index(request))
        prompt = build_risk_prompt(
            request,
            prompt_version=request.prompt_version,
            max_evidence_chars=self._settings.ai_max_evidence_chars,
            max_chars_per_chunk=min(
                request.max_chars_per_chunk or self._settings.ai_max_chars_per_chunk,
                self._settings.ai_max_chars_per_chunk,
            ),
        )

        result, attempts, validation_status, raw = await self._generate_validated(
            prompt, evidence_ids, schema=RiskAnalysisResult, grounding_check=_check_grounding
        )

        latency_ms = int((time.perf_counter() - started) * 1000)
        usage = self._build_usage(prompt, validation_status, attempts, latency_ms, raw)
        logger.info(
            "analysis_completed",
            extra={
                "model": usage.model,
                "promptVersion": usage.prompt_version,
                "latencyMs": usage.latency_ms,
                "validationStatus": usage.validation_status,
                "attempts": usage.repair_attempts + 1,
                "retrieved": len(request.retrieved_documents),
            },
        )
        return RiskAnalysisResponse(analysis_type="change-risk", result=result, usage=usage, trace=trace)

    async def analyze_incident(self, request: IncidentAnalysisRequest) -> IncidentAnalysisResponse:
        """Incident investigation (brief §2/§13–20): hybrid retrieval from the incident
        context -> layered prompt -> provider -> Pydantic validation -> deterministic
        grounding (root-cause candidates must reference real evidence ids) -> response.
        """
        started = time.perf_counter()
        logger.info(
            "incident_analysis_started",
            extra={"projectId": request.project_id, "analysisId": request.analysis_id},
        )

        request, trace = await self._maybe_retrieve_incident(request)

        evidence_ids = set(build_incident_evidence_index(request))
        tool_mode = bool(request.tool_catalog)
        schema: type[BaseModel] = IncidentTurnResult if tool_mode else IncidentAnalysisResult
        grounding = _check_turn_grounding if tool_mode else _check_incident_grounding

        prompt = build_incident_prompt(
            request,
            prompt_version=request.prompt_version,
            max_evidence_chars=self._settings.ai_max_evidence_chars,
            max_chars_per_chunk=min(
                request.max_chars_per_chunk or self._settings.ai_max_chars_per_chunk,
                self._settings.ai_max_chars_per_chunk,
            ),
        )

        result, attempts, validation_status, raw = await self._generate_validated(
            prompt, evidence_ids, schema=schema, grounding_check=grounding,
        )

        latency_ms = int((time.perf_counter() - started) * 1000)
        usage = self._build_usage(prompt, validation_status, attempts, latency_ms, raw)
        logger.info(
            "incident_analysis_completed",
            extra={
                "model": usage.model,
                "promptVersion": usage.prompt_version,
                "latencyMs": usage.latency_ms,
                "validationStatus": usage.validation_status,
                "retrieved": len(request.retrieved_documents),
                "toolMode": tool_mode,
                "toolResults": len(request.tool_results),
            },
        )
        if tool_mode:
            turn: IncidentTurnResult = result  # type: ignore[assignment]
            return IncidentAnalysisResponse(
                analysis_type="incident",
                kind=turn.kind,
                tool_call=turn.tool_call,
                result=turn.result,
                usage=usage,
                trace=trace,
            )
        return IncidentAnalysisResponse(analysis_type="incident", result=result, usage=usage, trace=trace)

    async def _maybe_retrieve_incident(
        self, request: IncidentAnalysisRequest
    ) -> tuple[IncidentAnalysisRequest, RetrievalTrace | None]:
        """Hybrid retrieval driven by the incident context (brief §13–14).

        Queries preserve exact technical identifiers (error messages, exception types,
        status codes, service names) — the keyword leg's 'simple' config matches them
        verbatim; the dependency leg additionally steers source/runbook/incident chunks
        by affected service and symbol-like terms from the symptom text.
        """
        if (
            not self._settings.ai_auto_retrieve
            or self._retrieval is None
            or request.retrieved_documents
        ):
            return request, None

        queries = [request.incident.title]
        for s in request.incident.symptoms[:5]:
            if s and s not in queries:
                queries.append(s)
        if request.incident.service and request.incident.service not in queries:
            queries.append(request.incident.service)

        dependency = DependencyRetrieval(
            services=[request.incident.service] if request.incident.service else [],
            symbols=_symbol_like_terms(request.incident.symptoms + [request.incident.title]),
        )

        max_chunks = min(
            request.max_evidence_chunks or self._settings.ai_max_evidence_chunks,
            self._settings.ai_max_evidence_chunks,
        )
        per_chunk_cap = min(
            request.max_chars_per_chunk or self._settings.ai_max_chars_per_chunk,
            self._settings.ai_max_chars_per_chunk,
        )
        # Request more candidates than we select so the trace can show what was
        # considered vs what entered the prompt (evidence-selection trace, brief §22).
        candidate_k = min(max_chunks * 2, self._settings.retrieval_candidate_k)
        hits = await asyncio.to_thread(
            self._retrieval.search_queries,
            request.project_id,
            queries,
            dependency=dependency,
            k=candidate_k,
        )
        selected = hits[:max_chunks]

        request.retrieved_documents = [
            RetrievedDocumentItem(
                id=f"chunk:{hit.chunk_id}",
                document_type=hit.document_type,
                title=hit.metadata.get("title"),
                content=hit.content[:per_chunk_cap],
                metadata={
                    "path": hit.metadata.get("path"),
                    "service": hit.metadata.get("service"),
                    "incidentId": hit.metadata.get("incidentId"),
                    "chunkType": hit.chunk_type,
                    "score": hit.score,
                    "dependency": bool(hit.metadata.get("dependency")),
                },
                score=hit.score,
            )
            for hit in selected
        ]
        trace = _build_retrieval_trace(queries, hits, selected, max_chunks, per_chunk_cap)
        logger.info(
            "incident_analysis_retrieved",
            extra={"chunks": len(request.retrieved_documents), "maxChunks": max_chunks},
        )
        return request, trace

    async def _maybe_retrieve(
        self, request: RiskAnalysisRequest
    ) -> tuple[RiskAnalysisRequest, RetrievalTrace | None]:
        """Fill the evidence package with hybrid retrieval when the request lacks it.

        Queries: the change summary plus changed-file basenames (exact technical terms
        are the keyword leg's strength). Results become retrieved documents with stable
        evidence ids `chunk:<uuid>` that the grounding rule enforces.
        """
        if (
            not self._settings.ai_auto_retrieve
            or self._retrieval is None
            or request.retrieved_documents
        ):
            return request, None

        # Text queries: change summary + changed-file basenames + changed/impacted symbol
        # names (exact technical terms are the keyword leg's strength).
        queries = [request.change_summary]
        for f in request.changed_files[:5]:
            name = f.path.rsplit("/", 1)[-1].rsplit("\\", 1)[-1]
            if name:
                queries.append(name)
        for s in request.changed_symbols[:20]:
            if s.name and s.name not in queries:
                queries.append(s.name)

        # Phase 4 dependency leg: the Roslyn-derived paths/symbols/services steer which
        # source/incident/runbook chunks enter the candidate set (rag-architecture §5).
        dependency = DependencyRetrieval(
            symbols=[s.name for s in request.changed_symbols[:100] if s.name],
            paths=request.dependency_paths[:200],
            services=request.impacted_services[:100],
        )

        max_chunks = min(
            request.max_evidence_chunks or self._settings.ai_max_evidence_chunks,
            self._settings.ai_max_evidence_chunks,
        )
        per_chunk_cap = min(
            request.max_chars_per_chunk or self._settings.ai_max_chars_per_chunk,
            self._settings.ai_max_chars_per_chunk,
        )
        # Candidate set > evidence budget: the trace records what was considered vs
        # what entered the prompt (evidence-selection trace, brief §22).
        candidate_k = min(max_chunks * 2, self._settings.retrieval_candidate_k)
        hits = await asyncio.to_thread(
            self._retrieval.search_queries,
            request.project_id,
            queries,
            dependency=dependency,
            k=candidate_k,
        )
        selected = hits[:max_chunks]

        request.retrieved_documents = [
            RetrievedDocumentItem(
                id=f"chunk:{hit.chunk_id}",
                document_type=hit.document_type,
                title=hit.metadata.get("title"),
                content=hit.content[:per_chunk_cap],
                metadata={
                    "path": hit.metadata.get("path"),
                    "service": hit.metadata.get("service"),
                    "chunkType": hit.chunk_type,
                    "score": hit.score,
                    "dependency": bool(hit.metadata.get("dependency")),
                },
                score=hit.score,
            )
            for hit in selected
        ]
        trace = _build_retrieval_trace(queries, hits, selected, max_chunks, per_chunk_cap)
        logger.info(
            "analysis_retrieved",
            extra={"chunks": len(request.retrieved_documents), "maxChunks": max_chunks},
        )
        return request, trace

    async def _generate_validated(
        self,
        prompt: PromptBundle,
        evidence_ids: set[str],
        *,
        schema: type[BaseModel],
        grounding_check: callable,
    ) -> tuple[BaseModel, int, str, StructuredResult]:
        """Provider call + validation + bounded repair. Raises AiValidationError on failure."""
        attempts = 0
        last_errors: list[str] = []

        while True:
            attempts += 1
            raw = await self._call_provider(prompt, schema)

            parsed, errors = self._validate(raw, evidence_ids, schema, grounding_check)
            if parsed is not None and not errors:
                status = "valid" if attempts == 1 else "repaired"
                logger.info("validation_result", extra={"status": status, "attempts": attempts})
                return parsed, attempts, status, raw

            last_errors = errors or ["model returned no usable structured output"]
            logger.info(
                "validation_failed",
                extra={"status": "failed", "attempts": attempts, "errors": last_errors},
            )

            if attempts > self._settings.ai_max_repair_attempts:
                logger.warning("safe_failure", extra={"attempts": attempts})
                raise AiValidationError(
                    "AI output failed validation after bounded repair.",
                    details={"attempts": attempts, "errors": last_errors},
                )

            prompt = build_repair_prompt(prompt, raw.content or "", last_errors)

    async def _call_provider(
        self, prompt: PromptBundle, schema: type[BaseModel] = RiskAnalysisResult
    ) -> StructuredResult:
        logger.info(
            "provider_call_started",
            extra={"model": getattr(self._provider, "model", None), "promptVersion": prompt.version},
        )
        try:
            raw = await self._provider.complete_structured(
                system=prompt.system,
                messages=prompt.messages,
                response_schema=schema,
                prompt_version=prompt.version,
            )
        except ProviderRateLimited as exc:
            logger.warning("provider_rate_limited")
            raise AiRateLimitedError("The AI provider is rate limited; try again shortly.") from exc
        except ProviderTimeout as exc:
            logger.warning("provider_timeout")
            raise AiTimeoutError("The AI provider timed out.") from exc
        except ProviderUnavailable as exc:
            logger.error("provider_unavailable", exc_info=exc)
            raise AiProviderError("The AI provider is temporarily unavailable.") from exc
        logger.info(
            "provider_call_completed",
            extra={
                "latencyMs": raw.latency_ms,
                "model": raw.model,
                "finishReason": raw.finish_reason,
            },
        )
        return raw

    def _validate(
        self,
        raw: StructuredResult,
        evidence_ids: set[str],
        schema: type[BaseModel],
        grounding_check: callable,
    ) -> tuple[BaseModel | None, list[str]]:
        """Strict Pydantic validation + deterministic post-checks. Returns (parsed, errors)."""
        parsed: BaseModel | None = None
        errors: list[str] = []

        candidate = raw.parsed
        if candidate is None and raw.content:
            try:
                candidate = schema.model_validate_json(raw.content)
            except ValidationError as exc:
                errors.extend(_format_pydantic_errors(exc))

        if candidate is not None:
            try:
                parsed = schema.model_validate(
                    candidate.model_dump() if not isinstance(candidate, dict) else candidate
                )
            except ValidationError as exc:
                errors.extend(_format_pydantic_errors(exc))

        if parsed is not None:
            errors.extend(grounding_check(parsed, evidence_ids))

        return parsed, errors

    def _build_usage(
        self,
        prompt: PromptBundle,
        validation_status: str,
        attempts: int,
        latency_ms: int,
        raw: StructuredResult,
    ) -> AnalysisUsage:
        # Tokens come from provider usage metadata (null = unknown, never guessed).
        # Cost is an *estimate* computed only when per-model pricing is configured.
        cost: float | None = None
        input_price = self._settings.gemini_input_price_per_1m_usd
        output_price = self._settings.gemini_output_price_per_1m_usd
        input_tokens = raw.usage.input_tokens
        output_tokens = raw.usage.output_tokens
        if input_price is not None and output_price is not None:
            cost = round(
                (input_tokens or 0) / 1_000_000 * input_price
                + (output_tokens or 0) / 1_000_000 * output_price,
                6,
            )

        return AnalysisUsage(
            model=raw.model or getattr(self._provider, "model", None),
            prompt_version=prompt.version,
            latency_ms=latency_ms,
            input_tokens=input_tokens,
            output_tokens=output_tokens,
            total_tokens=raw.usage.total_tokens,
            estimated_cost_usd=cost,
            validation_status=validation_status,
            repair_attempts=attempts - 1,
            evidence_truncated=prompt.evidence_truncated,
        )


def _build_retrieval_trace(
    queries: list[str],
    hits: list,
    selected: list,
    max_chunks: int,
    per_chunk_cap: int,
) -> RetrievalTrace:
    """Evidence-selection trace from the merged retrieval hits (brief §21–22).

    ``hits`` is the full merged candidate list (candidate_count); ``selected`` is the
    slice that actually entered the evidence package (selected_count). Each item
    carries its leg attribution from `sources` — vector similarity score, keyword
    rank, dependency rank — which are NOT comparable to each other.
    """
    return RetrievalTrace(
        queries=queries,
        candidate_count=len(hits),
        selected_count=len(selected),
        max_chunks=max_chunks,
        max_chars_per_chunk=per_chunk_cap,
        items=[
            RetrievalTraceItem(
                id=f"chunk:{hit.chunk_id}",
                document_type=hit.document_type,
                title=hit.metadata.get("title") if hit.metadata else None,
                path=hit.metadata.get("path") if hit.metadata else None,
                score=hit.score,
                vector_score=hit.sources.vector if hit.sources else None,
                keyword_rank=hit.sources.keyword if hit.sources else None,
                dependency_rank=hit.sources.dependency if hit.sources else None,
            )
            for hit in hits
        ],
    )


def _check_turn_grounding(
    turn: IncidentTurnResult, evidence_ids: set[str]
) -> list[str]:
    """Grounding for a tool-loop turn: only the final result is a claim.

    A tool proposal carries no claims, so nothing to ground; the final result must
    satisfy the standard incident grounding rule against the (tool-extended) index.
    """
    if turn.kind != "final" or turn.result is None:
        return []
    return _check_incident_grounding(turn.result, evidence_ids)


def _check_incident_grounding(
    result: IncidentAnalysisResult, evidence_ids: set[str]
) -> list[str]:
    """Deterministic incident grounding rule (brief §17, ADR-0007).

    - every root-cause candidate must reference >=1 evidence id that exists in the
      evidence index (empty evidence_ids is already rejected by Pydantic min_length)
    - the top-level evidence list must only contain input-package ids (no invented items)
    """
    errors: list[str] = []
    for i, candidate in enumerate(result.root_cause_candidates):
        if not any(eid in evidence_ids for eid in candidate.evidence_ids):
            errors.append(
                f"root_cause_candidates[{i}] references no evidence id from the evidence index."
            )
    for i, item in enumerate(result.evidence):
        if item.id not in evidence_ids:
            errors.append(f"evidence[{i}] id '{item.id}' is not in the evidence index.")
    return errors


# Symbol-like terms from symptom text: CamelCase names (TimeoutException,
# PaymentGatewayClient) that may match chunk symbols in the dependency leg. Acronyms
# (JWT, HTTP) and digits are left to the keyword leg's exact-match full-text search.
_IDENTIFIER_RE = re.compile(r"\b[A-Z][A-Za-z0-9]*[a-z][A-Za-z0-9]*\b")


def _symbol_like_terms(texts: list[str]) -> list[str]:
    terms: list[str] = []
    for text in texts:
        for match in _IDENTIFIER_RE.findall(text or ""):
            if match not in terms:
                terms.append(match)
    return terms[:20]


def _check_grounding(result: RiskAnalysisResult, evidence_ids: set[str]) -> list[str]:
    """Deterministic grounding rule (llm-integration.md §3, ADR-0007).

    - every risk factor must reference >=1 evidence id that exists in the input package
    - the top-level evidence list must only contain input-package ids (no invented items)
    """
    errors: list[str] = []
    for i, factor in enumerate(result.risk_factors):
        refs = [e.reference for e in factor.evidence]
        if not any(r in evidence_ids for r in refs):
            errors.append(f"risk_factors[{i}] references no evidence id from the evidence index.")
    for i, item in enumerate(result.evidence):
        if item.id not in evidence_ids:
            errors.append(f"evidence[{i}] id '{item.id}' is not in the evidence index.")
    return errors


def _format_pydantic_errors(exc: ValidationError) -> list[str]:
    return [f"{'.'.join(str(p) for p in e['loc'])}: {e['msg']}" for e in exc.errors()]
