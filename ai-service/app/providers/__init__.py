"""Provider selection (AI_PROVIDER env)."""

from __future__ import annotations

from ..config import Settings
from .base import IAIProvider
from .gemini import GeminiProvider
from .mock import MockAIProvider


def build_provider(settings: Settings) -> IAIProvider:
    """Create the configured provider. Raises ValueError on invalid configuration."""
    if settings.ai_provider == "mock":
        return MockAIProvider()
    if settings.ai_provider == "gemini":
        return GeminiProvider(
            api_key=settings.gemini_api_key or "",
            model=settings.gemini_text_model,
            timeout_seconds=settings.gemini_timeout_seconds,
            max_retries=settings.gemini_max_retries,
            max_output_tokens=settings.gemini_max_output_tokens,
        )
    raise ValueError(f"Unknown AI_PROVIDER: {settings.ai_provider!r}")
