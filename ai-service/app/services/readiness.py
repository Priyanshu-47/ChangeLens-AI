"""Readiness: configuration + provider/model resolvability.

Never consumes Gemini tokens: the default AI_READINESS_PROBE=false makes /ready a pure
config check. When the probe is enabled, it resolves the configured model name via the
API (metadata call, no generation).
"""

from __future__ import annotations

from ..config import Settings
from ..providers.base import IAIProvider


def readiness_payload(settings: Settings, provider: IAIProvider) -> dict:
    checks: dict = {
        "configuration": "ok",  # Settings construction already validated everything
        "provider": settings.ai_provider,
        "model": getattr(provider, "model", None),
        "readinessProbe": settings.ai_readiness_probe,
    }
    return {
        "status": "ready",
        "checks": checks,
    }


async def probe_model(provider: IAIProvider) -> bool:
    """Resolve the configured model name (metadata call only, zero tokens)."""
    resolve = getattr(provider, "resolve_model", None)
    if resolve is None:
        return True  # providers without a probe are treated as ready
    return await resolve()
