"""Analysis service: the structured-output pipeline (ADR-0007).

Flow:  prompt -> provider -> Pydantic validation -> deterministic post-checks
       (confidence bounds, array caps, grounding rule) -> SUCCESS
On failure: bounded repair (re-prompt with the exact validation errors, max N attempts),
then SAFE FAILURE (422 AI_VALIDATION_FAILED with attempt history). Unvalidated prose is
never returned as a result.
"""

from __future__ import annotations

import logging
import time

from pydantic import ValidationError

from ..config import Settings
from ..errors import AiProviderError, AiRateLimitedError, AiTimeoutError, AiValidationError
from ..llm.prompts import PromptBundle, build_evidence_index, build_repair_prompt, build_risk_prompt
from ..models.requests import RiskAnalysisRequest
from ..models.responses import AnalysisUsage, RiskAnalysisResponse, RiskAnalysisResult
from ..providers.base import (
    IAIProvider,
    ProviderRateLimited,
    ProviderTimeout,
    ProviderUnavailable,
    StructuredResult,
)

logger = logging.getLogger(__name__)


class AnalysisService:
    """Orchestrates one structured reasoning call per request (no RAG yet — Phase 3)."""

    def __init__(self, *, provider: IAIProvider, settings: Settings):
        self._provider = provider
        self._settings = settings

    @property
    def provider(self) -> IAIProvider:
        return self._provider

    async def analyze_change_risk(self, request: RiskAnalysisRequest) -> RiskAnalysisResponse:
        started = time.perf_counter()
        logger.info("analysis_started", extra={"projectId": request.project_id})

        evidence_ids = set(build_evidence_index(request))
        prompt = build_risk_prompt(
            request,
            prompt_version=request.prompt_version,
            max_evidence_chars=self._settings.ai_max_evidence_chars,
        )

        result, attempts, validation_status, raw = await self._generate_validated(
            prompt, evidence_ids
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
            },
        )
        return RiskAnalysisResponse(analysis_type="change-risk", result=result, usage=usage)

    async def _generate_validated(
        self, prompt: PromptBundle, evidence_ids: set[str]
    ) -> tuple[RiskAnalysisResult, int, str, StructuredResult]:
        """Provider call + validation + bounded repair. Raises AiValidationError on failure."""
        attempts = 0
        last_errors: list[str] = []

        while True:
            attempts += 1
            raw = await self._call_provider(prompt)

            parsed, errors = self._validate(raw, evidence_ids)
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

    async def _call_provider(self, prompt: PromptBundle) -> StructuredResult:
        logger.info(
            "provider_call_started",
            extra={"model": getattr(self._provider, "model", None), "promptVersion": prompt.version},
        )
        try:
            raw = await self._provider.complete_structured(
                system=prompt.system,
                messages=prompt.messages,
                response_schema=RiskAnalysisResult,
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
        self, raw: StructuredResult, evidence_ids: set[str]
    ) -> tuple[RiskAnalysisResult | None, list[str]]:
        """Strict Pydantic validation + deterministic post-checks. Returns (parsed, errors)."""
        parsed: RiskAnalysisResult | None = None
        errors: list[str] = []

        candidate = raw.parsed
        if candidate is None and raw.content:
            try:
                candidate = RiskAnalysisResult.model_validate_json(raw.content)
            except ValidationError as exc:
                errors.extend(_format_pydantic_errors(exc))

        if candidate is not None:
            try:
                parsed = RiskAnalysisResult.model_validate(
                    candidate.model_dump() if not isinstance(candidate, dict) else candidate
                )
            except ValidationError as exc:
                errors.extend(_format_pydantic_errors(exc))

        if parsed is not None:
            errors.extend(_check_grounding(parsed, evidence_ids))

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
