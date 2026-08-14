"""Internal API (`/internal/v1`), shared-secret auth (ai-service-boundary.md §3).

Every request must carry:
  X-Internal-Key: <INTERNAL_API_KEY>
  X-Contract-Version: 1
Rejected calls get the uniform error envelope; the ASP.NET backend remains the authority
for user authentication, RBAC, and project authorization.
"""

from __future__ import annotations

import secrets

from fastapi import APIRouter, Depends, Header, Request

from ..config import Settings
from ..errors import ContractVersionError, InternalAuthError
from ..models.requests import RiskAnalysisRequest
from ..models.responses import RiskAnalysisResponse
from ..providers.base import IAIProvider
from ..services.analysis_service import AnalysisService
from ..services.readiness import probe_model, readiness_payload

router = APIRouter(prefix="/internal/v1", tags=["internal"])


def require_internal_auth(
    request: Request,
    x_internal_key: str | None = Header(default=None, alias="X-Internal-Key"),
    x_contract_version: str | None = Header(default=None, alias="X-Contract-Version"),
) -> None:
    # The shared secret always comes from the app's validated settings, never from
    # request data. (The .NET backend passes it as X-Internal-Key.)
    expected = request.app.state.settings.internal_api_key
    if x_internal_key is None or not secrets.compare_digest(x_internal_key, expected):
        raise InternalAuthError("Missing or invalid X-Internal-Key.")
    if x_contract_version != "1":
        raise ContractVersionError("X-Contract-Version must be '1'.")


def _analysis_service(request: Request) -> AnalysisService:
    return request.app.state.analysis_service


@router.get("/health/live", dependencies=[Depends(require_internal_auth)])
async def live(request: Request) -> dict:
    settings: Settings = request.app.state.settings
    return {"status": "ok", "service": settings.app_name, "checks": {"process": "ok"}}


@router.get("/health/ready", dependencies=[Depends(require_internal_auth)])
async def ready(request: Request):
    settings: Settings = request.app.state.settings
    from fastapi.responses import JSONResponse

    provider: IAIProvider = request.app.state.provider
    payload = readiness_payload(settings, provider)
    is_ready = True
    if settings.ai_readiness_probe and settings.ai_provider == "gemini":
        payload["checks"]["modelResolvable"] = await probe_model(provider)
        if not payload["checks"]["modelResolvable"]:
            payload["status"] = "not_ready"
            is_ready = False
    return JSONResponse(status_code=200 if is_ready else 503, content=payload)


@router.post(
    "/analysis/risk",
    response_model=RiskAnalysisResponse,
    dependencies=[Depends(require_internal_auth)],
)
async def analyze_risk(
    request: RiskAnalysisRequest, fastapi_request: Request
) -> RiskAnalysisResponse:
    return await _analysis_service(fastapi_request).analyze_change_risk(request)
