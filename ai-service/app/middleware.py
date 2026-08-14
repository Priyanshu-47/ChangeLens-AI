"""Correlation id propagation (brief §25).

Reads X-Correlation-ID from the caller (.NET backend) or generates one. The value is
attached to logs, error envelopes, and the response header, so a single analysis is
traceable across React -> .NET -> FastAPI -> Gemini.
"""

from __future__ import annotations

import uuid

from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request
from starlette.responses import Response

from .logging_conf import correlation_id_var


class CorrelationMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next) -> Response:
        correlation_id = request.headers.get("X-Correlation-ID") or str(uuid.uuid4())
        token = correlation_id_var.set(correlation_id)
        try:
            response = await call_next(request)
        finally:
            correlation_id_var.reset(token)
        response.headers["X-Correlation-ID"] = correlation_id
        return response
