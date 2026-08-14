"""Gemini provider (MVP) via the official google-genai SDK.

Uses the current SDK pattern: `client.aio.models.generate_content` with
`response_schema` (a Pydantic model) for native structured outputs — no deprecated
sampling parameters. Provider-specific details are normalized inside this adapter;
callers only see Pydantic models in and out (ADR-0005).
"""

from __future__ import annotations

import asyncio
import logging
import time

from google import genai
from google.genai import errors as genai_errors
from google.genai import types

from .base import (
    ProviderAuthError,
    ProviderBadRequest,
    ProviderRateLimited,
    ProviderTimeout,
    ProviderUnavailable,
    ProviderUsage,
    StructuredResult,
)
from .retry import with_retry

logger = logging.getLogger(__name__)


class GeminiProvider:
    """Structured-output adapter over the Gemini API (google-genai SDK)."""

    def __init__(
        self,
        *,
        api_key: str,
        model: str,
        timeout_seconds: float = 60.0,
        max_retries: int = 3,
        max_output_tokens: int = 8192,
    ):
        if not api_key:
            raise ValueError("GeminiProvider requires an API key.")
        self._model = model
        self._max_retries = max_retries
        self._max_output_tokens = max_output_tokens
        # The SDK accepts a timeout in milliseconds via http_options.
        self._client = genai.Client(
            api_key=api_key,
            http_options=types.HttpOptions(timeout=int(timeout_seconds * 1000)),
        )

    @property
    def model(self) -> str:
        return self._model

    @property
    def provider_name(self) -> str:
        return "gemini"

    async def complete_structured(
        self,
        *,
        system: str,
        messages: list[dict[str, str]],
        response_schema: type,
        prompt_version: str | None = None,
    ) -> StructuredResult:
        contents = [{"role": m.get("role", "user"), "parts": [{"text": m["content"]}]} for m in messages]
        config = types.GenerateContentConfig(
            system_instruction=system,
            response_mime_type="application/json",
            response_schema=response_schema,
            max_output_tokens=self._max_output_tokens,
        )

        started = time.perf_counter()

        async def call():
            try:
                return await self._client.aio.models.generate_content(
                    model=self._model,
                    contents=contents,
                    config=config,
                )
            except Exception as exc:  # normalize to the provider error vocabulary
                raise self._classify(exc) from exc

        try:
            response = await with_retry(
                call,
                max_retries=self._max_retries,
                is_retryable=self._classify_retryable,
                retry_after=self._retry_after,
            )
        except ProviderError:
            raise

        latency_ms = int((time.perf_counter() - started) * 1000)
        usage = response.usage_metadata
        return StructuredResult(
            content=response.text,
            parsed=response.parsed if isinstance(response.parsed, response_schema) else None,
            usage=ProviderUsage(
                input_tokens=usage.prompt_token_count if usage else None,
                output_tokens=usage.candidates_token_count if usage else None,
                total_tokens=usage.total_token_count if usage else None,
            ),
            latency_ms=latency_ms,
            model=self._model,
            finish_reason=response.finish_reason.name if response.finish_reason else None,
        )

    async def resolve_model(self) -> bool:
        """True when the configured model is resolvable (readiness probe, no tokens used).

        Called only when AI_READINESS_PROBE=true — never on /health or at startup.
        """
        try:
            await self._client.aio.models.get(model=self._model)
            return True
        except Exception as exc:  # noqa: BLE001 - any failure means "not resolvable"
            logger.warning("Readiness probe: model %s not resolvable: %s", self._model, type(exc).__name__)
            return False

    # --- classification helpers ---

    def _classify_retryable(self, exc: Exception) -> bool:
        # 429 and 5xx are transient; auth/request/timeout errors are not retried.
        return isinstance(exc, (ProviderRateLimited, ProviderUnavailable))

    def _retry_after(self, exc: Exception) -> float | None:
        if isinstance(exc, ProviderRateLimited):
            return exc.retry_after_seconds
        return None

    def _classify(self, exc: Exception) -> Exception:
        """Map an SDK exception to the normalized provider error vocabulary."""
        if isinstance(exc, ProviderError):
            return exc
        if isinstance(exc, genai_errors.ClientError):
            code = getattr(exc, "code", None)
            if code == 429:
                return ProviderRateLimited(
                    "Gemini rate limit exceeded.", retry_after_seconds=extract_retry_after(exc)
                )
            if code in (400, 401, 403):
                return ProviderAuthError(f"Gemini rejected credentials/request (HTTP {code}).")
            if code is not None and 500 <= code < 600:
                return ProviderUnavailable(f"Gemini returned HTTP {code}.")
            return ProviderBadRequest(f"Gemini rejected the request (HTTP {code or 'unknown'}).")
        if isinstance(exc, genai_errors.APIError):
            code = getattr(exc, "code", None)
            if code == 429:
                return ProviderRateLimited(
                    "Gemini rate limit exceeded.", retry_after_seconds=extract_retry_after(exc)
                )
            if code in (408, 504):
                return ProviderTimeout("Gemini timed out.")
            if code is not None and 500 <= code < 600:
                return ProviderUnavailable(f"Gemini returned HTTP {code}.")
            return ProviderUnavailable(f"Gemini API error (HTTP {code or 'unknown'}).")
        if isinstance(exc, asyncio.TimeoutError):
            return ProviderTimeout("Gemini call exceeded the configured timeout.")
        if isinstance(exc, (ConnectionError, TimeoutError)):
            return ProviderUnavailable(f"Gemini transport error: {type(exc).__name__}.")
        # Unknown SDK bug — still sanitized, still a provider failure.
        return ProviderUnavailable(f"Gemini SDK error: {type(exc).__name__}.")


def extract_retry_after(exc: Exception) -> float | None:
    """Best-effort Retry-After from the SDK error. None when absent."""
    try:
        raw = getattr(exc, "response", None)
        if raw is not None and hasattr(raw, "headers"):
            value = raw.headers.get("Retry-After")
            if value:
                return float(value)
    except (TypeError, ValueError):
        pass
    return None
