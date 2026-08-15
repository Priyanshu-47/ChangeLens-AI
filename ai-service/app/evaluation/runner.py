"""Deterministic evaluation runner (docs/evaluation.md).

For every golden case and every enabled leg (vector / keyword / dependency /
hybrid) it runs one retrieval search against the seeded corpus and computes
Recall@K / Precision@K / MRR / Hit Rate against the gold evidence annotations.
Optionally it runs the full mock-AI analysis pipeline per case and records
schema validity, grounding validity, and evidence coverage.

Everything here is deterministic and runs with mock providers — zero Gemini
calls, no API key. The retrieval service is injected (duck-typed) so the runner
is testable without a database; the CLI (app/evaluation/run.py) wires the real
RetrievalService + AnalysisService.
"""

from __future__ import annotations

import logging
import time
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from typing import Callable, Sequence

from ..models.requests import DependencyRetrieval, RetrievalSearchRequest
from ..models.responses import RetrievalSearchResponse
from .dataset import GoldenCase, source_key, gold_match_key
from .metrics import average, fraction, hit_rate, leg_contribution, mrr, precision_at_k, recall_at_k

logger = logging.getLogger(__name__)

#: Legs the evaluator understands (must map to RetrievalSearchRequest.strategy).
LEGS = ("vector", "keyword", "dependency", "hybrid")

#: Legs that need dependency terms (query-derived symbol-like identifiers).
DEPENDENCY_LEGS = ("dependency", "hybrid")


def _symbol_like_terms(texts: Sequence[str]) -> list[str]:
    """CamelCase identifiers from the query, reused by the incident flow.

    Mirrors analysis_service._symbol_like_terms (kept here to avoid a circular
    import); the dependency leg only matters when the query carries identifiers.
    """
    import re

    terms: list[str] = []
    pattern = re.compile(r"\b[A-Z][A-Za-z0-9]*[a-z][A-Za-z0-9]*\b")
    for text in texts:
        for match in pattern.findall(text or ""):
            if match not in terms:
                terms.append(match)
    return terms[:20]


@dataclass
class LegScore:
    k: int
    recall: float
    precision: float
    mrr: float
    hit: float


@dataclass
class LegCaseResult:
    leg: str
    status: str = "evaluated"  # "evaluated" | "skipped"
    skipped_reason: str | None = None
    k_values: list[int] = field(default_factory=list)
    scores: list[LegScore] = field(default_factory=list)
    retrieved_keys: list[str] = field(default_factory=list)
    candidates: list[dict] = field(default_factory=list)  # per-item leg attribution


@dataclass
class AiCaseResult:
    status: str = "evaluated"  # "evaluated" | "skipped"
    skipped_reason: str | None = None
    validation_status: str | None = None
    repair_attempts: int | None = None
    latency_ms: int | None = None
    grounded: bool | None = None
    evidence_cited: list[str] = field(default_factory=list)
    gold_covered: list[str] = field(default_factory=list)
    coverage: float | None = None
    model: str | None = None
    prompt_version: str | None = None
    queries: list[str] = field(default_factory=list)


@dataclass
class ToolsCaseResult:
    """Phase 8 tool-loop measurement at the AI-service boundary (docs/agent-tools.md §9).

    Python never executes tools, so this measures what the AI service CAN prove:
    proposal validity (name in the catalog), deterministic loop completion, and
    grounding of the final result after tool results were fed back. Tool
    authorization and rejection are .NET behaviors covered by integration tests.
    """

    status: str = "skipped"  # "evaluated" | "skipped"
    skipped_reason: str | None = None
    proposals: int = 0
    proposals_valid: int = 0
    loop_completed: bool | None = None
    grounding_after_tools: bool | None = None
    tool_names: list[str] = field(default_factory=list)


@dataclass
class CaseResult:
    case_id: str
    workflow: str
    query: str
    gold_evidence: list[str]
    difficulty: str | None
    archetype: str | None
    latency_ms: int
    legs: dict[str, LegCaseResult] = field(default_factory=dict)
    ai: AiCaseResult = field(default_factory=AiCaseResult)
    tools: ToolsCaseResult = field(default_factory=ToolsCaseResult)
    error: str | None = None


def _request_for(
    case: GoldenCase,
    leg: str,
    k: int,
    project_id: str,
    symbol_terms: list[str],
) -> RetrievalSearchRequest:
    dependency = DependencyRetrieval(symbols=symbol_terms) if leg in DEPENDENCY_LEGS else None
    return RetrievalSearchRequest(
        project_id=project_id,
        query=case.query,
        strategy=leg,  # type: ignore[arg-type]  # "dependency" added for evaluation
        k=k,
        dependency=dependency,
    )


class EvaluationRunner:
    """Runs the golden dataset through retrieval (+ optional AI) and builds reports."""

    def __init__(
        self,
        *,
        retrieval: object,
        project_id: str,
        k_values: Sequence[int] = (5, 10),
        legs: Sequence[str] = LEGS,
        dataset_version: str = "v1",
        ai_pipeline: bool = True,
        analysis: object | None = None,
        embedding_model: str | None = None,
        embedding_dimension: int | None = None,
        ai_model: str | None = None,
    ):
        for leg in legs:
            if leg not in LEGS:
                raise ValueError(f"Unknown leg {leg!r}; expected one of {LEGS}.")
        self._retrieval = retrieval
        self._analysis = analysis
        self._project_id = project_id
        self._k_values = [int(k) for k in k_values]
        self._legs = list(legs)
        self._dataset_version = dataset_version
        self._ai_pipeline = ai_pipeline and analysis is not None
        self._embedding_model = embedding_model
        self._embedding_dimension = embedding_dimension
        self._ai_model = ai_model

    # --- case-level evaluation -------------------------------------------------

    def evaluate_case(self, case: GoldenCase) -> CaseResult:
        started = time.perf_counter()
        result = CaseResult(
            case_id=case.id,
            workflow="retrieval",
            query=case.query,
            gold_evidence=list(case.expected_evidence),
            difficulty=case.difficulty,
            archetype=case.archetype,
            latency_ms=0,
        )
        relevant = {e for e in case.expected_evidence}
        symbol_terms = _symbol_like_terms([case.query])

        try:
            for leg in self._legs:
                result.legs[leg] = self._evaluate_leg(case, leg, relevant, symbol_terms)
        except Exception as exc:  # noqa: BLE001 — record per-case failure, keep the run alive
            logger.exception("case %s failed", case.id)
            result.error = f"{type(exc).__name__}: {exc}"

        result.latency_ms = int((time.perf_counter() - started) * 1000)

        if self._ai_pipeline and result.error is None:
            result.ai = self._evaluate_ai(case)
            result.tools = self._evaluate_tools(case)
        elif not self._ai_pipeline:
            result.ai.status = "skipped"
            result.ai.skipped_reason = "ai_pipeline disabled"
            result.tools.status = "skipped"
            result.tools.skipped_reason = "ai_pipeline disabled"
        return result

    def _evaluate_leg(
        self,
        case: GoldenCase,
        leg: str,
        relevant: set[str],
        symbol_terms: list[str],
    ) -> LegCaseResult:
        leg_result = LegCaseResult(leg=leg)
        if leg == "dependency" and not symbol_terms:
            # The dependency leg is change-model-driven; a bare query without
            # identifiers cannot exercise it. Hybrid still runs (vector + keyword).
            leg_result.status = "skipped"
            leg_result.skipped_reason = "no dependency terms derivable from the query"
            return leg_result
        if not relevant:
            leg_result.status = "skipped"
            leg_result.skipped_reason = "no gold evidence annotations for this case"
            return leg_result

        k_max = max(self._k_values)
        response = self._search(_request_for(case, leg, k_max, self._project_id, symbol_terms))

        keys: list[str] = []
        candidates: list[dict] = []
        seen_keys: set[str] = set()
        for item in response.results:
            key = source_key(item.source, item.metadata.get("title") if item.metadata else None)
            candidates.append(
                {
                    "id": item.chunk_id,
                    "documentType": item.document_type,
                    "key": key,
                    "score": item.score,
                    "vectorScore": item.sources.vector if item.sources else None,
                    "keywordRank": item.sources.keyword if item.sources else None,
                    "dependencyRank": item.sources.dependency if item.sources else None,
                }
            )
            # Relevance is at the source/document level: several chunks of the same
            # gold document are ONE hit. Dedupe keys (first rank wins) before metrics.
            if key and key not in seen_keys:
                seen_keys.add(key)
                keys.append(key)

        leg_result.status = "evaluated"
        leg_result.retrieved_keys = keys
        leg_result.candidates = candidates
        for k in self._k_values:
            leg_result.scores.append(
                LegScore(
                    k=k,
                    recall=recall_at_k(keys, relevant, k),
                    precision=precision_at_k(keys, relevant, k),
                    mrr=mrr(keys, relevant),
                    hit=hit_rate(keys, relevant, k),
                )
            )
        return leg_result

    def _evaluate_ai(self, case: GoldenCase) -> AiCaseResult:
        """Run the mock-AI change-risk pipeline over the hybrid package.

        Deterministic: MockAIProvider output is fixed for a given evidence set, and
        the grounding validator is mechanical. We record schema validity, grounding
        validity, and evidence coverage (fraction of gold sources cited by the AI).
        """
        if self._analysis is None:
            return AiCaseResult(status="skipped", skipped_reason="analysis service not provided")

        from ..models.requests import ChangedFile, ChangedSymbolItem, RiskAnalysisRequest

        symbols = [
            ChangedSymbolItem(
                symbol_id=f"synthetic:{name}", kind="Class", name=name,
                fully_qualified_name=name,
            )
            for name in _symbol_like_terms([case.query])
        ]
        request = RiskAnalysisRequest(
            project_id=self._project_id,
            change_summary=case.query,
            changed_files=[
                ChangedFile(path="evaluation/synthetic.cs", change_type="modified", language="csharp")
            ],
            changed_symbols=symbols,
        )

        started = time.perf_counter()
        try:
            import asyncio

            response = asyncio.run(self._analysis.analyze_change_risk(request))  # type: ignore[attr-defined]
        except Exception as exc:  # noqa: BLE001
            return AiCaseResult(status="skipped", skipped_reason=f"analysis raised {type(exc).__name__}: {exc}")

        latency_ms = int((time.perf_counter() - started) * 1000)
        usage = response.usage
        cited = [e.reference for e in response.result.evidence]

        # Map cited evidence ids (chunk:<uuid>) to source keys via the retrieval
        # trace (path/title per retrieved chunk). Unmappable ids simply don't count
        # toward coverage — never treated as matches.
        chunk_key: dict[str, str | None] = {}
        trace = getattr(response, "trace", None)
        if trace is not None:
            for item in trace.items:
                chunk_key[item.id] = source_key(item.path, item.title)
        cited_keys = {k for eid in cited for k in [chunk_key.get(eid)] if k}
        covered = [
            g for g in case.expected_evidence
            if any(gold_match_key(g, {k}) for k in cited_keys)
        ]
        coverage = fraction(len(covered), len(case.expected_evidence))

        return AiCaseResult(
            status="evaluated",
            validation_status=usage.validation_status,
            repair_attempts=usage.repair_attempts,
            latency_ms=latency_ms,
            grounded=usage.validation_status in ("valid", "repaired"),
            evidence_cited=cited,
            gold_covered=covered,
            coverage=coverage,
            model=usage.model,
            prompt_version=usage.prompt_version,
            queries=list(trace.queries) if trace is not None else [],
        )

    def _evaluate_tools(self, case: GoldenCase) -> ToolsCaseResult:
        """Deterministic mock tool loop (docs/agent-tools.md §9).

        Drives the incident analysis through the three mock turns (propose dependency
        paths -> propose runbook -> final) with the allowlist catalog, feeding the
        deterministic tool outputs back between turns, and records proposal validity,
        loop completion, and grounding of the final result. Zero Gemini calls.
        """
        if self._analysis is None:
            return ToolsCaseResult(status="skipped", skipped_reason="analysis service not provided")

        from ..models.requests import (
            IncidentAnalysisRequest,
            IncidentContextItem,
            ToolDefinition,
            ToolResultItem,
        )

        catalog = [
            ToolDefinition(name="get_incident", description="Incident record", input_schema={}),
            ToolDefinition(name="get_incident_timeline", description="Chronological events", input_schema={}),
            ToolDefinition(name="get_service", description="Service record", input_schema={}),
            ToolDefinition(name="get_runbook", description="Runbook retrieval", input_schema={}),
            ToolDefinition(name="get_source_symbol", description="Source retrieval", input_schema={}),
            ToolDefinition(name="get_dependency_paths", description="Dependency graph traversal", input_schema={}),
            ToolDefinition(name="search_evidence", description="Hybrid retrieval", input_schema={}),
        ]
        names = {t.name for t in catalog}

        def request_with(results: list[ToolResultItem]) -> IncidentAnalysisRequest:
            return IncidentAnalysisRequest(
                project_id=self._project_id,
                analysis_id="eval-tools",
                incident=IncidentContextItem(
                    title=case.query,
                    severity="Sev1",
                    status="Open",
                    symptoms=[case.query],
                ),
                tool_catalog=catalog,
                tool_results=results,
            )

        import asyncio
        import json as _json

        results: list[ToolResultItem] = []
        names_seen: list[str] = []
        proposals = 0
        proposals_valid = 0

        try:
            turn1 = asyncio.run(self._analysis.analyze_incident(request_with(results)))  # type: ignore[attr-defined]
            if turn1.kind != "tool_call" or turn1.tool_call is None:
                return ToolsCaseResult(
                    status="evaluated", proposals=0, proposals_valid=0,
                    loop_completed=False, grounding_after_tools=False,
                )
            proposals += 1
            proposals_valid += int(turn1.tool_call.name in names)
            names_seen.append(turn1.tool_call.name)
            results.append(
                ToolResultItem(
                    tool_call_id=turn1.tool_call.id,
                    tool_name=turn1.tool_call.name,
                    status="executed",
                    output=_json.dumps(
                        {
                            "symbol": turn1.tool_call.arguments.get("symbol", "TokenService"),
                            "evidenceIds": [
                                "dependency:AcmePay.Auth.TokenService -> AcmePay.Program"
                            ],
                            "paths": [],
                        }
                    ),
                )
            )

            turn2 = asyncio.run(self._analysis.analyze_incident(request_with(results)))  # type: ignore[attr-defined]
            if turn2.kind != "tool_call" or turn2.tool_call is None:
                return ToolsCaseResult(
                    status="evaluated", proposals=proposals,
                    proposals_valid=proposals_valid, loop_completed=False,
                    grounding_after_tools=False, tool_names=names_seen,
                )
            proposals += 1
            proposals_valid += int(turn2.tool_call.name in names)
            names_seen.append(turn2.tool_call.name)
            results.append(
                ToolResultItem(
                    tool_call_id=turn2.tool_call.id,
                    tool_name=turn2.tool_call.name,
                    status="executed",
                    output=_json.dumps(
                        {
                            "query": case.query,
                            "evidenceIds": ["chunk:eval-runbook-1"],
                            "items": [{"id": "chunk:eval-runbook-1", "content": "Rotate keys."}],
                        }
                    ),
                )
            )

            turn3 = asyncio.run(self._analysis.analyze_incident(request_with(results)))  # type: ignore[attr-defined]
        except Exception as exc:  # noqa: BLE001 — record per-case skip, keep the run alive
            return ToolsCaseResult(
                status="skipped", skipped_reason=f"tool loop raised {type(exc).__name__}: {exc}",
            )

        loop_completed = turn3.kind == "final" and turn3.result is not None
        grounding = (
            turn3.usage.validation_status in ("valid", "repaired")
            if turn3.kind == "final"
            else False
        )
        return ToolsCaseResult(
            status="evaluated",
            proposals=proposals,
            proposals_valid=proposals_valid,
            loop_completed=loop_completed,
            grounding_after_tools=grounding,
            tool_names=names_seen,
        )

    def _search(self, request: RetrievalSearchRequest) -> RetrievalSearchResponse:
        return self._retrieval.search(request)  # type: ignore[attr-defined]

    # --- aggregation + report ---------------------------------------------------

    def summarize(self, case_results: Sequence[CaseResult]) -> dict:
        evaluated = [c for c in case_results if c.error is None]
        legs: dict[str, dict] = {}
        for leg in self._legs:
            evaluated_leg = [c.legs[leg] for c in evaluated if leg in c.legs and c.legs[leg].status == "evaluated"]
            skipped_leg = [c for c in evaluated if leg in c.legs and c.legs[leg].status == "skipped"]
            per_k: dict[str, dict] = {}
            for k in self._k_values:
                recall = average(s.recall for c in evaluated_leg for s in c.scores if s.k == k)
                precision = average(s.precision for c in evaluated_leg for s in c.scores if s.k == k)
                mrr_avg = average(s.mrr for c in evaluated_leg for s in c.scores if s.k == k)
                hit = average(s.hit for c in evaluated_leg for s in c.scores if s.k == k)
                per_k[str(k)] = {"recall@k": recall, "precision@k": precision, "mrr": mrr_avg, "hit_rate": hit}
            contribution = leg_contribution(
                [item for c in evaluated if "hybrid" in c.legs for item in c.legs["hybrid"].candidates],
                leg,
            )
            skip_reasons: dict[str, int] = {}
            for c in skipped_leg:
                reason = c.legs[leg].skipped_reason or "unspecified"
                skip_reasons[reason] = skip_reasons.get(reason, 0) + 1
            legs[leg] = {
                "evaluated": len(evaluated_leg),
                "skipped": len(skipped_leg),
                "skipReasons": skip_reasons,
                "perK": per_k,
                "hybridContribution": contribution,
            }

        ai_evaluated = [c.ai for c in evaluated if c.ai.status == "evaluated"]
        ai = {
            "pipelineEnabled": self._ai_pipeline,
            "evaluated": len(ai_evaluated),
            "schemaValid": sum(1 for a in ai_evaluated if a.validation_status in ("valid", "repaired")),
            "grounded": sum(1 for a in ai_evaluated if a.grounded),
            "coverageAverage": average(a.coverage for a in ai_evaluated if a.coverage is not None),
            "repairAttemptsAverage": average(a.repair_attempts or 0 for a in ai_evaluated),
            "latencyMsAverage": average(a.latency_ms or 0 for a in ai_evaluated),
        }

        tools_evaluated = [c.tools for c in evaluated if c.tools.status == "evaluated"]
        tools = {
            "evaluated": len(tools_evaluated),
            "proposals": sum(t.proposals for t in tools_evaluated),
            "proposalsValid": sum(t.proposals_valid for t in tools_evaluated),
            "proposalValidity": fraction(
                sum(t.proposals_valid for t in tools_evaluated),
                sum(t.proposals for t in tools_evaluated),
            ),
            "loopCompleted": sum(1 for t in tools_evaluated if t.loop_completed),
            "groundingAfterTools": sum(1 for t in tools_evaluated if t.grounding_after_tools),
            "toolsUsed": sorted(
                {name for t in tools_evaluated for name in t.tool_names}
            ),
        }

        skipped = [c for c in case_results if c.error is not None]
        return {
            "casesTotal": len(case_results),
            "casesEvaluated": len(evaluated),
            "casesFailed": len(skipped),
            "legs": legs,
            "ai": ai,
            "tools": tools,
        }

    def build_report(
        self,
        cases: Sequence[GoldenCase],
        case_results: Sequence[CaseResult],
    ) -> dict:
        return {
            "evaluationRunId": str(uuid.uuid4()),
            "datasetVersion": self._dataset_version,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "config": {
                "projectId": self._project_id,
                "kValues": self._k_values,
                "legs": self._legs,
                "aiPipeline": self._ai_pipeline,
                "embeddingModel": self._embedding_model,
                "embeddingDimension": self._embedding_dimension,
                "aiModel": self._ai_model,
            },
            "summary": self.summarize(case_results),
            "cases": [asdict(c) for c in case_results],
        }
