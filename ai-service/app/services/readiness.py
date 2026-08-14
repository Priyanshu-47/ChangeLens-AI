"""Readiness: configuration + database + vector extension + provider resolvability.

Never consumes Gemini tokens: the default AI_READINESS_PROBE=false keeps /ready free
of provider calls. The DB checks are cheap SQL (SELECT 1, extension lookup) — exactly
what readiness is for (brief §48).
"""

from __future__ import annotations

from sqlalchemy import text

from ..config import Settings
from ..db import Database
from ..providers.base import IAIProvider


def readiness_payload(settings: Settings, provider: IAIProvider, db: Database | None = None) -> dict:
    checks: dict = {
        "configuration": "ok",  # Settings construction already validated everything
        "provider": settings.ai_provider,
        "model": getattr(provider, "model", None),
        "embeddingProvider": settings.embedding_provider,
        "embeddingModel": settings.gemini_embedding_model,
        "readinessProbe": settings.ai_readiness_probe,
    }
    status = "ready"

    if db is not None:
        database_ok, vector_ok, error = _db_checks(db)
        checks["database"] = "ok" if database_ok else f"unreachable: {error}"
        checks["vectorExtension"] = vector_ok
        if not database_ok or not vector_ok:
            status = "not_ready"

    checks["databaseConfigured"] = db is not None
    return {"status": status, "checks": checks}


def _db_checks(db: Database) -> tuple[bool, bool, str | None]:
    try:
        with db.session() as session:
            session.execute(text("SELECT 1"))
            row = session.execute(
                text("SELECT 1 FROM pg_extension WHERE extname = 'vector'")
            ).first()
            return True, row is not None, None
    except Exception as exc:  # noqa: BLE001 - readiness must never crash
        return False, False, type(exc).__name__


async def probe_model(provider: IAIProvider) -> bool:
    """Resolve the configured model name (metadata call only, zero tokens)."""
    resolve = getattr(provider, "resolve_model", None)
    if resolve is None:
        return True  # providers without a probe are treated as ready
    return await resolve()
