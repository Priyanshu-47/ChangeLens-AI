"""Provider abstraction (ADR-0005).

The analysis service depends on `IAIProvider` only. `GeminiProvider` is the MVP
implementation; `MockAIProvider` is the deterministic stand-in for local dev and tests.
Future providers (OpenAI, Bedrock) implement the same protocol without touching
orchestration, validation, or persistence.

The protocol deliberately declares only capabilities that have consumers today.
`embed_texts` joins in Phase 3 (embeddings) — adding a stub method now would be a
placeholder feature, not an interface.
"""

from __future__ import annotations

from typing import Protocol, runtime_checkable

from pydantic import BaseModel


class ProviderUsage(BaseModel):
    """Token counts reported by the provider. Null means "not exposed" — never guessed."""

    input_tokens: int | None = None
    output_tokens: int | None = None
    total_tokens: int | None = None


class StructuredResult(BaseModel):
    """Normalized outcome of a structured completion."""

    content: str | None = None
    parsed: BaseModel | None = None
    usage: ProviderUsage = ProviderUsage()
    latency_ms: int | None = None
    model: str | None = None
    finish_reason: str | None = None


class ProviderError(Exception):
    """Base for provider failures. Raised by adapters; mapped to HTTP by the service."""


class ProviderRateLimited(ProviderError):
    def __init__(self, message: str, *, retry_after_seconds: float | None = None):
        super().__init__(message)
        self.retry_after_seconds = retry_after_seconds


class ProviderUnavailable(ProviderError):
    """Provider returned a 5xx or the connection failed (transient — retryable)."""


class ProviderTimeout(ProviderError):
    """Provider did not answer within the configured timeout (NOT blindly retried)."""


class ProviderAuthError(ProviderError):
    """API key invalid / rejected (never retried)."""


class ProviderBadRequest(ProviderError):
    """Provider rejected the request itself (never retried)."""


@runtime_checkable
class IAIProvider(Protocol):
    """Adapters must satisfy this protocol. See llm-integration.md §1."""

    async def complete_structured(
        self,
        *,
        system: str,
        messages: list[dict[str, str]],
        response_schema: type[BaseModel],
        prompt_version: str | None = None,
    ) -> StructuredResult:
        ...
