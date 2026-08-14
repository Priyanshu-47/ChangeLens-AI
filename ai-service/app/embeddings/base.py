"""Embedding provider abstraction (ADR-0006).

`GeminiEmbeddingProvider` is the MVP; `MockEmbeddingProvider` is the deterministic
zero-cost stand-in for dev/tests. A future local provider (sentence-transformers)
or another API provider implements the same protocol without touching ingestion,
retrieval, or persistence.

Embedding calls happen ONLY during ingestion/re-indexing and query-time vector
search — never at startup, on health checks, or in the normal test suite.
"""

from __future__ import annotations

from typing import Protocol, runtime_checkable

from pydantic import BaseModel, Field


class EmbeddingResult(BaseModel):
    """Normalized batch embedding outcome."""

    vectors: list[list[float]]
    model: str
    model_version: str
    dimension: int
    input_tokens: int | None = Field(default=None)
    latency_ms: int | None = Field(default=None)


class EmbeddingError(Exception):
    """Base for embedding provider failures."""


class EmbeddingRateLimited(EmbeddingError):
    def __init__(self, message: str, *, retry_after_seconds: float | None = None):
        super().__init__(message)
        self.retry_after_seconds = retry_after_seconds


class EmbeddingUnavailable(EmbeddingError):
    """Transient provider failure (retryable)."""


class EmbeddingAuthError(EmbeddingError):
    """API key rejected (never retried)."""


class EmbeddingDimensionError(EmbeddingError):
    """Provider returned vectors of the wrong dimension."""


@runtime_checkable
class IEmbeddingProvider(Protocol):
    @property
    def model(self) -> str: ...

    @property
    def model_version(self) -> str: ...

    @property
    def dimension(self) -> int: ...

    def embed_texts(self, texts: list[str]) -> EmbeddingResult: ...
