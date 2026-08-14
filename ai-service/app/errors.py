"""AI service error contract.

Every HTTP failure returns the same envelope shape used by the public API:

    {"type", "title", "status", "detail", "code", "traceId", "details"}

Status code mapping (docs/ai-service-boundary.md §3-4):
  400 INVALID_REQUEST          malformed request body / contract version
  401 UNAUTHORIZED_INTERNAL    missing or wrong X-Internal-Key
  422 AI_VALIDATION_FAILED     structured output failed validation after bounded repair
  429 LLM_RATE_LIMITED         provider rate limit (no blind retry inside the request)
  502 AI_PROVIDER_ERROR        provider unavailable / SDK error (sanitized)
  504 AI_TIMEOUT               provider did not answer within the configured timeout
  500 INTERNAL_ERROR           unexpected bug — never a raw provider stack trace
"""

from __future__ import annotations

from typing import Any

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field


class ErrorEnvelope(BaseModel):
    type: str
    title: str
    status: int
    detail: str
    code: str
    traceId: str | None = None
    details: dict[str, Any] | None = Field(default=None)


class AiError(Exception):
    """Base class for expected, user-facing AI service failures."""

    status_code = 500
    code = "INTERNAL_ERROR"
    title = "An error occurred"

    def __init__(self, detail: str, *, details: dict[str, Any] | None = None):
        super().__init__(detail)
        self.detail = detail
        self.details = details


class AiRequestError(AiError):
    status_code = 400
    code = "INVALID_REQUEST"
    title = "Invalid request"


class InternalAuthError(AiError):
    status_code = 401
    code = "UNAUTHORIZED_INTERNAL"
    title = "Unauthorized"


class ContractVersionError(AiError):
    status_code = 400
    code = "INVALID_CONTRACT_VERSION"
    title = "Invalid contract version"


class AiValidationError(AiError):
    status_code = 422
    code = "AI_VALIDATION_FAILED"
    title = "AI output failed validation"


class AiRateLimitedError(AiError):
    status_code = 429
    code = "LLM_RATE_LIMITED"
    title = "Provider rate limited"


class AiTimeoutError(AiError):
    status_code = 504
    code = "AI_TIMEOUT"
    title = "Provider timed out"


class AiProviderError(AiError):
    status_code = 502
    code = "AI_PROVIDER_ERROR"
    title = "Provider error"


def _envelope(exc: AiError, trace_id: str | None) -> ErrorEnvelope:
    return ErrorEnvelope(
        type=f"https://api.changelens.dev/errors/{exc.code.lower()}",
        title=exc.title,
        status=exc.status_code,
        detail=exc.detail,
        code=exc.code,
        traceId=trace_id,
        details=exc.details,
    )


def register_exception_handlers(app: FastAPI, trace_id_provider) -> None:
    """Attach envelope-producing handlers. trace_id_provider returns the current correlation id."""

    @app.exception_handler(AiError)
    async def handle_ai_error(request: Request, exc: AiError) -> JSONResponse:
        envelope = _envelope(exc, trace_id_provider())
        return JSONResponse(
            status_code=envelope.status,
            content=envelope.model_dump(mode="json", exclude_none=True),
        )

    @app.exception_handler(RequestValidationError)
    async def handle_validation_error(
        request: Request, exc: RequestValidationError
    ) -> JSONResponse:
        errors = [
            {"loc": [str(p) for p in e.get("loc", [])], "msg": e.get("msg", ""), "type": e.get("type", "")}
            for e in exc.errors()
        ]
        envelope = ErrorEnvelope(
            type="https://api.changelens.dev/errors/invalid_request",
            title="Invalid request",
            status=400,
            detail="Request body failed validation.",
            code="INVALID_REQUEST",
            traceId=trace_id_provider(),
            details={"errors": errors},
        )
        return JSONResponse(status_code=400, content=envelope.model_dump(mode="json", exclude_none=True))

    @app.exception_handler(Exception)
    async def handle_unexpected(request: Request, exc: Exception) -> JSONResponse:
        # Safe failure: never leak provider/SDK details to the caller.
        envelope = ErrorEnvelope(
            type="https://api.changelens.dev/errors/internal_error",
            title="An error occurred",
            status=500,
            detail="Unexpected internal error.",
            code="INTERNAL_ERROR",
            traceId=trace_id_provider(),
        )
        return JSONResponse(status_code=500, content=envelope.model_dump(mode="json", exclude_none=True))
