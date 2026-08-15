"""Embedding provider selection (EMBEDDING_PROVIDER env)."""

from __future__ import annotations

from ..config import Settings
from .base import (
    EmbeddingAuthError,
    EmbeddingDimensionError,
    EmbeddingError,
    EmbeddingRateLimited,
    EmbeddingResult,
    EmbeddingUnavailable,
    IEmbeddingProvider,
)
from .gemini import GeminiEmbeddingProvider
from .mock import MockEmbeddingProvider

__all__ = [
    "EmbeddingAuthError",
    "EmbeddingDimensionError",
    "EmbeddingError",
    "EmbeddingRateLimited",
    "EmbeddingResult",
    "EmbeddingUnavailable",
    "GeminiEmbeddingProvider",
    "IEmbeddingProvider",
    "MockEmbeddingProvider",
]


def build_embedding_provider(settings: Settings) -> IEmbeddingProvider:
    """Create the configured embedding provider. Raises ValueError on invalid config."""
    if settings.embedding_provider == "mock":
        # The mock's model label tracks the configured real model so a model change is
        # visible in model_version (and therefore triggers a re-embed) even in mock mode.
        return MockEmbeddingProvider(
            dimension=settings.embedding_dimension,
            model=f"mock-{settings.gemini_embedding_model}",
        )
    if settings.embedding_provider == "gemini":
        return GeminiEmbeddingProvider(
            api_key=settings.gemini_api_key or "",
            model=settings.gemini_embedding_model,
            dimension=settings.embedding_dimension,
            batch_size=settings.embedding_batch_size,
            timeout_seconds=settings.gemini_timeout_seconds,
            max_retries=settings.embedding_batch_max_retries,
        )
    raise ValueError(f"Unknown EMBEDDING_PROVIDER: {settings.embedding_provider!r}")
