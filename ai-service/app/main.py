"""ChangeLens AI service entrypoint.

  GET  /health                       liveness (process up, no dependencies)
  GET  /ready                        readiness (config valid; probe only if enabled)
  GET  /internal/v1/health/live      internal liveness (X-Internal-Key)
  GET  /internal/v1/health/ready     internal readiness (X-Internal-Key)
  POST /internal/v1/analysis/risk    structured change-risk analysis

Startup performs ZERO Gemini calls; Gemini is only contacted when an analysis request
arrives (or when AI_READINESS_PROBE=true hits /ready).
"""

from __future__ import annotations

from fastapi import FastAPI

from . import __version__
from .api.internal import router as internal_router
from .config import Settings, get_settings
from .errors import register_exception_handlers
from .logging_conf import correlation_id_var, setup_logging
from .middleware import CorrelationMiddleware
from .providers import build_provider
from .services.analysis_service import AnalysisService
from .services.readiness import readiness_payload


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or get_settings()
    setup_logging(settings.log_level)

    provider = build_provider(settings)  # raises ValueError on invalid configuration
    analysis_service = AnalysisService(provider=provider, settings=settings)

    app = FastAPI(
        title="ChangeLens AI Service",
        version=__version__,
        description="Internal AI capability service: structured reasoning over evidence packages.",
    )

    app.state.settings = settings
    app.state.provider = provider
    app.state.analysis_service = analysis_service

    app.add_middleware(CorrelationMiddleware)
    register_exception_handlers(app, trace_id_provider=lambda: correlation_id_var.get())

    app.include_router(internal_router)

    @app.get("/health", tags=["ops"])
    async def health() -> dict:
        return {"status": "ok", "service": settings.app_name, "checks": {"process": "ok"}}

    @app.get("/ready", tags=["ops"])
    async def ready():
        from fastapi.responses import JSONResponse

        payload = readiness_payload(settings, provider)
        is_ready = True
        if settings.ai_readiness_probe and settings.ai_provider == "gemini":
            from .services.readiness import probe_model

            payload["checks"]["modelResolvable"] = await probe_model(provider)
            if not payload["checks"]["modelResolvable"]:
                payload["status"] = "not_ready"
                is_ready = False
        return JSONResponse(status_code=200 if is_ready else 503, content=payload)

    return app


app = create_app()
