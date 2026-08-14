"""Live Gemini smoke test — never runs in the default suite.

Enable explicitly with:
    RUN_GEMINI_TESTS=true GEMINI_API_KEY=... pytest tests/test_gemini_live.py -v

One minimal structured-output call proves the full path:
provider -> Gemini -> Pydantic-validated RiskAnalysisResult -> grounding check.
"""

from __future__ import annotations

import os

import pytest

from app.config import Settings
from app.models.requests import RiskAnalysisRequest
from app.models.responses import RiskLevel
from app.providers import build_provider
from app.services.analysis_service import AnalysisService

pytestmark = pytest.mark.skipif(
    os.environ.get("RUN_GEMINI_TESTS") != "true" or not os.environ.get("GEMINI_API_KEY"),
    reason="Set RUN_GEMINI_TESTS=true and GEMINI_API_KEY to run live Gemini tests",
)


@pytest.mark.asyncio
async def test_live_gemini_structured_risk_analysis():
    settings = Settings(
        internal_api_key="test-internal-key",
        ai_provider="gemini",
        gemini_api_key=os.environ["GEMINI_API_KEY"],
        gemini_text_model=os.environ.get("GEMINI_TEXT_MODEL", "gemini-3.7-flash"),
        ai_max_repair_attempts=2,
    )
    provider = build_provider(settings)
    service = AnalysisService(provider=provider, settings=settings)

    request = RiskAnalysisRequest(
        project_id="p1",
        change_summary="Changed token refresh logic in AuthClient.cs to use a rotated signing key.",
        changed_files=[
            {
                "path": "src/AuthClient.cs",
                "change_type": "modified",
                "language": "csharp",
                "symbols_changed": ["RefreshAsync"],
            }
        ],
    )

    response = await service.analyze_change_risk(request)

    assert response.result.risk_level in RiskLevel
    assert 0.0 <= response.result.confidence <= 1.0
    # Grounding is enforced server-side: every factor references an input evidence id.
    for factor in response.result.risk_factors:
        assert any(
            e.reference == "change:src/AuthClient.cs" for e in factor.evidence
        ), f"factor {factor.title!r} is not grounded"
    assert response.usage.validation_status in {"valid", "repaired"}
    assert response.usage.model
    print(f"\nLIVE SMOKE OK model={response.usage.model} "
          f"tokens={response.usage.total_tokens} latency={response.usage.latency_ms}ms")
