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
        gemini_text_model=os.environ.get("GEMINI_TEXT_MODEL", "gemini-3.1-flash-lite"),
        ai_max_repair_attempts=2,
    )
    provider = build_provider(settings)
    service = AnalysisService(provider=provider, settings=settings)

    request = RiskAnalysisRequest(
        project_id="p1",
        change_summary="JWT signing key rotation was modified in TokenService. Assess potential release risk.",
        changed_files=[
            {
                "path": "src/AcmePay.Application/Auth/TokenService.cs",
                "change_type": "modified",
                "language": "csharp",
                "symbols_changed": ["IssueServiceToken", "SigningKeys"],
            }
        ],
        runbooks=[
            {
                "id": "authentication-failure",
                "title": "Authentication failure runbook",
                "content": "When callers see 401 invalid signature after a key rotation, confirm the previous "
                "signing key remains in Auth:JwtSigningKeys until all in-flight tokens expire.",
            }
        ],
        historical_incidents=[
            {
                "incident_id": "auth-001-jwt-key-rotation",
                "reference": "incidents/auth-001.md",
                "summary": "401s across services after a signing key was rotated without keeping the previous key in the history.",
            }
        ],
    )

    response = await service.analyze_change_risk(request)

    assert response.result.risk_level in RiskLevel
    assert 0.0 <= response.result.confidence <= 1.0
    # Grounding is enforced server-side: every factor references an input evidence id.
    for factor in response.result.risk_factors:
        assert any(
            e.reference == "change:src/AcmePay.Application/Auth/TokenService.cs"
            for e in factor.evidence
        ), f"factor {factor.title!r} is not grounded"
    assert response.usage.validation_status in {"valid", "repaired"}
    assert response.usage.model
    print(f"\nLIVE SMOKE OK model={response.usage.model} "
          f"tokens={response.usage.total_tokens} latency={response.usage.latency_ms}ms")


@pytest.mark.asyncio
async def test_live_gemini_embedding_dimension():
    """Real embedding call: prove the configured model + dimension contract."""
    from app.embeddings import GeminiEmbeddingProvider

    settings = Settings(
        internal_api_key="test-internal-key",
        ai_provider="gemini",
        gemini_api_key=os.environ["GEMINI_API_KEY"],
        embedding_provider="gemini",
        gemini_embedding_model=os.environ.get("GEMINI_EMBEDDING_MODEL", "gemini-embedding-2"),
    )
    provider = GeminiEmbeddingProvider(
        api_key=settings.gemini_api_key or "",
        model=settings.gemini_embedding_model,
        dimension=settings.embedding_dimension,
        batch_size=settings.embedding_batch_size,
        timeout_seconds=settings.gemini_timeout_seconds,
        max_retries=settings.embedding_batch_max_retries,
    )

    result = provider.embed_texts(["retry the payment gateway", "JWT signing key rotation"])

    assert len(result.vectors) == 2
    assert all(len(v) == provider.dimension for v in result.vectors)
    assert result.model == provider.model
    assert result.model_version == provider.model_version
    print(f"\nLIVE EMBEDDING OK model={provider.model} dim={provider.dimension} "
          f"tokens={result.input_tokens}")
