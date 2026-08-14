"""ChangeLens AI service entrypoint.

  GET  /health                       liveness (process up, no dependencies)
  GET  /ready                        readiness (config + DB + vector extension)
  GET  /internal/v1/health/live      internal liveness (X-Internal-Key)
  GET  /internal/v1/health/ready     internal readiness (X-Internal-Key)
  POST /internal/v1/analysis/risk    structured change-risk analysis (RAG-fed)
  POST /internal/v1/ingest/documents idempotent document ingestion
  POST /internal/v1/retrieval/search hybrid retrieval (vector + keyword + RRF)

Startup performs ZERO Gemini calls — text/embedding providers are only contacted by
analysis and ingestion/query-time embedding respectively. The database is also not
touched at startup; reachability is reported by /ready.
"""

from __future__ import annotations

from fastapi import FastAPI

from . import __version__
from .api.internal import router as internal_router
from .config import Settings, get_settings
from .db import Database, create_engine_for
from .embeddings import build_embedding_provider
from .errors import register_exception_handlers
from .ingestion.service import IngestionService
from .logging_conf import correlation_id_var, setup_logging
from .middleware import CorrelationMiddleware
from .providers import build_provider
from .retrieval.service import RetrievalService
from .services.analysis_service import AnalysisService
from .services.readiness import readiness_payload


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or get_settings()
    setup_logging(settings.log_level)

    provider = build_provider(settings)  # raises ValueError on invalid configuration
    embedding = build_embedding_provider(settings)

    # Engine creation is lazy — no DB connection happens here (unit tests stay DB-free).
    db = Database(create_engine_for(settings))
    ingestion_service = IngestionService(db=db, embedding=embedding, settings=settings)
    retrieval_service = RetrievalService(db=db, embedding=embedding, settings=settings)
    analysis_service = AnalysisService(
        provider=provider, settings=settings, retrieval=retrieval_service
    )

    app = FastAPI(
        title="ChangeLens AI Service",
        version=__version__,
        description="Internal AI capability service: structured reasoning + hybrid RAG over evidence.",
    )

    app.state.settings = settings
    app.state.provider = provider
    app.state.analysis_service = analysis_service
    app.state.ingestion_service = ingestion_service
    app.state.retrieval_service = retrieval_service
    app.state.db = db

    app.add_middleware(CorrelationMiddleware)
    register_exception_handlers(app, trace_id_provider=lambda: correlation_id_var.get())

    app.include_router(internal_router)

    @app.get("/health", tags=["ops"])
    async def health() -> dict:
        return {"status": "ok", "service": settings.app_name, "checks": {"process": "ok"}}

    @app.get("/ready", tags=["ops"])
    async def ready():
        from fastapi.responses import JSONResponse

        payload = readiness_payload(settings, provider, db)
        return JSONResponse(status_code=200 if payload["status"] == "ready" else 503, content=payload)

    return app


app = create_app()
