"""Analysis service behaviour with a scripted provider — no external AI involved."""

import pytest
from pydantic import BaseModel

from app.config import Settings
from app.errors import (
    AiProviderError,
    AiRateLimitedError,
    AiTimeoutError,
    AiValidationError,
)
from app.models.requests import RiskAnalysisRequest
from app.models.responses import (
    AnalysisUsage,
    EvidenceItem,
    EvidenceReference,
    EvidenceType,
    RiskAnalysisResult,
    RiskFactor,
    RiskLevel,
)
from app.providers.base import (
    ProviderRateLimited,
    ProviderTimeout,
    ProviderUnavailable,
    ProviderUsage,
    StructuredResult,
)
from app.services.analysis_service import AnalysisService


def make_settings(**overrides) -> Settings:
    base = {"internal_api_key": "test-internal-key", "ai_provider": "mock"}
    base.update(overrides)
    return Settings(**base)


def make_request(**overrides) -> RiskAnalysisRequest:
    data = {
        "project_id": "p1",
        "change_summary": "Changed token refresh logic in AuthClient.",
        "changed_files": [{"path": "src/AuthClient.cs", "change_type": "modified", "language": "csharp"}],
    }
    data.update(overrides)
    return RiskAnalysisRequest(**data)


def grounded_result() -> RiskAnalysisResult:
    return RiskAnalysisResult(
        risk_level=RiskLevel.MEDIUM,
        confidence=0.7,
        impacted_components=[{"name": "AuthClient", "file_path": "src/AuthClient.cs"}],
        risk_factors=[
            RiskFactor(
                id="rf-1",
                title="AuthClient modified",
                description="Token refresh logic changed; dependent callers may be affected.",
                severity=RiskLevel.MEDIUM,
                evidence=[EvidenceReference(type=EvidenceType.ChangedFile, reference="change:src/AuthClient.cs")],
            )
        ],
        historical_incidents=[],
        recommended_tests=[],
        unknowns=[],
        evidence=[
            EvidenceItem(
                id="change:src/AuthClient.cs",
                type=EvidenceType.ChangedFile,
                reference="src/AuthClient.cs",
                summary="Token refresh logic changed.",
            )
        ],
    )


class ScriptedProvider:
    """Deterministic provider playing a scripted sequence of results/exceptions."""

    def __init__(self, *results):
        self._queue = list(results)
        self.calls: list[dict] = []

    @property
    def model(self) -> str:
        return "scripted-model"

    async def complete_structured(self, **kwargs) -> StructuredResult:
        self.calls.append(kwargs)
        item = self._queue.pop(0)
        if isinstance(item, Exception):
            raise item
        return item


def ok_result(result: RiskAnalysisResult | None = None, *, content: str | None = None) -> StructuredResult:
    result = result or grounded_result()
    return StructuredResult(
        content=content or result.model_dump_json(),
        parsed=result,
        usage=ProviderUsage(input_tokens=120, output_tokens=40, total_tokens=160),
        latency_ms=25,
        model="scripted-model",
    )


def malformed_json_result() -> StructuredResult:
    return StructuredResult(content='{"riskLevel": "HIGH"', parsed=None, usage=ProviderUsage())


@pytest.mark.asyncio
async def test_valid_first_call():
    provider = ScriptedProvider(ok_result())
    service = AnalysisService(provider=provider, settings=make_settings())
    response = await service.analyze_change_risk(make_request())

    assert response.result.risk_level == RiskLevel.MEDIUM
    assert response.usage.validation_status == "valid"
    assert response.usage.repair_attempts == 0
    assert response.usage.model == "scripted-model"
    assert response.usage.input_tokens == 120
    assert response.usage.output_tokens == 40
    assert response.usage.total_tokens == 160
    assert response.usage.latency_ms is not None
    assert len(provider.calls) == 1


@pytest.mark.asyncio
async def test_repair_recovers_from_invalid_output():
    provider = ScriptedProvider(malformed_json_result(), ok_result())
    service = AnalysisService(provider=provider, settings=make_settings())
    response = await service.analyze_change_risk(make_request())

    assert response.usage.validation_status == "repaired"
    assert response.usage.repair_attempts == 1
    assert len(provider.calls) == 2
    # The second call received the repair turns.
    second_call = provider.calls[1]
    assert second_call["messages"][-1]["role"] == "user"
    assert "failed validation" in second_call["messages"][-1]["content"]


@pytest.mark.asyncio
async def test_safe_failure_after_bounded_repair():
    provider = ScriptedProvider(malformed_json_result(), malformed_json_result(), malformed_json_result())
    service = AnalysisService(provider=provider, settings=make_settings(ai_max_repair_attempts=2))
    with pytest.raises(AiValidationError) as exc_info:
        await service.analyze_change_risk(make_request())

    assert exc_info.value.details["attempts"] == 3  # initial + 2 repairs
    assert len(provider.calls) == 3


@pytest.mark.asyncio
async def test_grounding_violation_triggers_repair():
    ungrounded = grounded_result()
    ungrounded.risk_factors[0].evidence = [
        EvidenceReference(type=EvidenceType.Document, reference="src/other.cs#L1")
    ]
    provider = ScriptedProvider(ok_result(ungrounded), ok_result())
    service = AnalysisService(provider=provider, settings=make_settings())
    response = await service.analyze_change_risk(make_request())

    assert response.usage.validation_status == "repaired"
    repair_turn = provider.calls[1]["messages"][-1]["content"]
    assert "evidence index" in repair_turn


@pytest.mark.asyncio
async def test_provider_rate_limited_maps_to_429_error():
    provider = ScriptedProvider(ProviderRateLimited("limit"))
    service = AnalysisService(provider=provider, settings=make_settings())
    with pytest.raises(AiRateLimitedError):
        await service.analyze_change_risk(make_request())


@pytest.mark.asyncio
async def test_provider_timeout_maps_to_504_error():
    provider = ScriptedProvider(ProviderTimeout("slow"))
    service = AnalysisService(provider=provider, settings=make_settings())
    with pytest.raises(AiTimeoutError):
        await service.analyze_change_risk(make_request())


@pytest.mark.asyncio
async def test_provider_unavailable_maps_to_502_error():
    provider = ScriptedProvider(ProviderUnavailable("down"))
    service = AnalysisService(provider=provider, settings=make_settings())
    with pytest.raises(AiProviderError):
        await service.analyze_change_risk(make_request())


@pytest.mark.asyncio
async def test_cost_estimate_only_when_pricing_configured():
    settings = make_settings(
        gemini_input_price_per_1m_usd=0.15, gemini_output_price_per_1m_usd=0.60
    )
    provider = ScriptedProvider(ok_result())
    service = AnalysisService(provider=provider, settings=settings)
    response = await service.analyze_change_risk(make_request())
    # 120/1e6*0.15 + 40/1e6*0.60 = 0.000018 + 0.000024 = 0.000042
    assert response.usage.estimated_cost_usd == pytest.approx(0.000042, abs=1e-9)


@pytest.mark.asyncio
async def test_cost_is_none_without_pricing_config():
    provider = ScriptedProvider(ok_result())
    service = AnalysisService(provider=provider, settings=make_settings())
    response = await service.analyze_change_risk(make_request())
    assert response.usage.estimated_cost_usd is None


def test_usage_model_defaults():
    usage = AnalysisUsage()
    assert usage.input_tokens is None
    assert usage.estimated_cost_usd is None
    assert usage.validation_status == "valid"


@pytest.mark.asyncio
async def test_change_intelligence_evidence_flows_through_mock():
    """Phase 4: the mock provider emits symbol:/dependency: evidence grounded in the
    change-model context — the analyzer -> graph -> retrieval chain is observable
    without Gemini."""
    from app.llm.prompts import build_evidence_index
    from app.providers.mock import MockAIProvider

    symbol = {
        "symbol_id": "global::Auth.TokenService.Rotate()",
        "kind": "Method",
        "name": "Rotate",
        "fully_qualified_name": "global::Auth.TokenService.Rotate()",
    }
    edge = {
        "from_symbol_id": "global::Auth.TokenService.Rotate()",
        "to_symbol_id": "global::Auth.ApiKeyValidator.Validate()",
        "edge_type": "CALLS",
    }

    provider = MockAIProvider()
    service = AnalysisService(provider=provider, settings=make_settings())
    response = await service.analyze_change_risk(
        make_request(changed_symbols=[symbol], dependency_edges=[edge])
    )

    assert response.usage.validation_status == "valid"
    evidence_ids = {e.id for e in response.result.evidence}
    assert "symbol:global::Auth.TokenService.Rotate()" in evidence_ids
    assert (
        "dependency:global::Auth.TokenService.Rotate() -> global::Auth.ApiKeyValidator.Validate()"
        in evidence_ids
    )

    # Every risk factor references evidence ids from the index (grounding held).
    index = set(build_evidence_index(make_request(changed_symbols=[symbol], dependency_edges=[edge])))
    for factor in response.result.risk_factors:
        assert any(e.reference in index for e in factor.evidence)
