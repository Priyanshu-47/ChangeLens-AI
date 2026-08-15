"""Incident investigation (Phase 5): request validation, structured schema, grounding,
prompt construction, mock provider behaviour, and provider error mapping.

Zero Gemini calls: the normal suite uses the deterministic MockAIProvider / scripted
providers (brief §41).
"""

from __future__ import annotations

import pytest
from pydantic import ValidationError

from app.config import Settings
from app.errors import AiProviderError, AiRateLimitedError, AiTimeoutError, AiValidationError
from app.llm.prompts import build_incident_evidence_index, build_incident_prompt
from app.models.requests import (
    IncidentAnalysisRequest,
    IncidentContextItem,
    TimelineEventItem,
)
from app.models.responses import (
    CandidateStatus,
    IncidentAnalysisResult,
    IncidentEvidenceItem,
    Remediation,
    RootCauseCandidate,
)
from app.providers.base import (
    ProviderRateLimited,
    ProviderTimeout,
    ProviderUnavailable,
    ProviderUsage,
    StructuredResult,
)
from app.providers.mock import MockAIProvider
from app.services.analysis_service import (
    AnalysisService,
    _check_incident_grounding,
    _symbol_like_terms,
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


def make_settings(**overrides) -> Settings:
    base = {"internal_api_key": "test-internal-key", "ai_provider": "mock"}
    base.update(overrides)
    return Settings(**base)


def make_incident(**overrides) -> IncidentContextItem:
    data = {
        "title": "HTTP 401 after JWT signing-key rotation",
        "summary": "Authentication requests started returning 401 after the signing key changed.",
        "severity": "Sev1",
        "status": "Open",
        "environment": "production",
        "service": "acmepay-api",
        "started_at_utc": "2026-08-01T09:00:00Z",
        "detected_at_utc": "2026-08-01T09:05:00Z",
        "symptoms": [
            "System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler failed with 'IDX10503'",
            "401 Unauthorized from /api/v1/auth/token",
        ],
        "known_facts": ["Severity: Sev1", "Affected service: acmepay-api"],
        "unknowns": ["No deployment timestamp was supplied."],
        "timeline": [
            TimelineEventItem(
                occurred_at_utc="2026-08-01T08:55:00Z",
                type="deployment",
                message="Deployed TokenService signing-key rotation",
                source="cicd",
            ),
            TimelineEventItem(
                occurred_at_utc="2026-08-01T09:01:00Z",
                type="error",
                message="JwtSecurityTokenHandler: IDX10503 signature validation failed",
                source="api",
            ),
        ],
    }
    data.update(overrides)
    return IncidentContextItem(**data)


def make_request(**overrides) -> IncidentAnalysisRequest:
    data = {"project_id": "p1", "incident": make_incident()}
    data.update(overrides)
    return IncidentAnalysisRequest(**data)


def grounded_incident_result() -> IncidentAnalysisResult:
    return IncidentAnalysisResult(
        root_cause_candidates=[
            RootCauseCandidate(
                candidate_id="cand-1",
                title="Signing-key rotation invalidated issued tokens",
                description="Tokens issued before rotation no longer verify.",
                confidence=0.7,
                status=CandidateStatus.CANDIDATE,
                evidence_ids=["chunk:abc"],
                reasoning="The timeline places the deployment before the first 401.",
                unknowns=[],
            )
        ],
        remediation=Remediation(
            immediate_mitigation="Validate the new key against the token issuer.",
            investigation_steps=["Correlate first 401 with the rotation window."],
            recommended_remediation=None,
            validation_steps=[],
            rollback_consideration="Evaluate rolling the rotation back.",
            insufficient_evidence=False,
        ),
        unknowns=[],
        evidence=[
            IncidentEvidenceItem(id="chunk:abc", source="chunk:abc", summary="auth-001 incident chunk.")
        ],
    )


def ok_incident_result() -> StructuredResult:
    result = grounded_incident_result()
    return StructuredResult(
        content=result.model_dump_json(),
        parsed=result,
        usage=ProviderUsage(input_tokens=90, output_tokens=30, total_tokens=120),
        latency_ms=20,
        model="scripted-model",
    )


# --- schema / validation ---


def test_incident_request_requires_project_and_incident():
    with pytest.raises(ValidationError):
        IncidentAnalysisRequest(project_id="p1")  # missing incident
    with pytest.raises(ValidationError):
        IncidentAnalysisRequest(incident=make_incident())  # missing project_id


def test_root_cause_candidate_rejects_empty_evidence_ids():
    with pytest.raises(ValidationError):
        RootCauseCandidate(
            candidate_id="cand-1",
            title="t",
            description="d",
            confidence=0.5,
            evidence_ids=[],  # grounding rule: at least one evidence id
        )


def test_root_cause_candidate_rejects_out_of_range_confidence():
    with pytest.raises(ValidationError):
        RootCauseCandidate(
            candidate_id="cand-1", title="t", description="d", confidence=1.5, evidence_ids=["chunk:abc"]
        )
    with pytest.raises(ValidationError):
        RootCauseCandidate(
            candidate_id="cand-1", title="t", description="d", confidence=-0.1, evidence_ids=["chunk:abc"]
        )


def test_remediation_defaults_to_insufficient_evidence_false():
    remediation = Remediation()
    assert remediation.insufficient_evidence is False
    assert remediation.investigation_steps == []


def test_incident_result_defaults():
    result = IncidentAnalysisResult()
    assert result.root_cause_candidates == []
    assert result.unknowns == []
    assert result.evidence == []
    assert result.remediation.insufficient_evidence is False


# --- grounding (deterministic post-checks) ---


def test_grounding_rejects_candidate_with_unknown_evidence_id():
    index = {"chunk:abc"}
    result = grounded_incident_result()
    result.root_cause_candidates[0].evidence_ids = ["chunk:made-up"]
    errors = _check_incident_grounding(result, index)
    assert any("root_cause_candidates[0]" in e for e in errors)


def test_grounding_rejects_invented_evidence_item():
    index = {"chunk:abc"}
    result = grounded_incident_result()
    result.evidence.append(IncidentEvidenceItem(id="chunk:invented", source="x"))
    errors = _check_incident_grounding(result, index)
    assert any("chunk:invented" in e for e in errors)


def test_grounding_passes_for_grounded_result():
    index = {"chunk:abc"}
    assert _check_incident_grounding(grounded_incident_result(), index) == []


# --- prompt construction ---


def test_incident_prompt_contains_incident_context_and_evidence_index():
    request = make_request(
        retrieved_documents=[
            {"id": "chunk:abc", "document_type": "Incident", "content": "auth-001 jwt key rotation"},
            {"id": "chunk:def", "document_type": "Runbook", "content": "authentication-failure runbook"},
        ]
    )
    prompt = build_incident_prompt(request, prompt_version="incident-v1")
    assert prompt.version == "incident-v1"
    assert "HTTP 401 after JWT signing-key rotation" in prompt.messages[0]["content"]
    assert "deployment: Deployed TokenService signing-key rotation" in prompt.messages[0]["content"]
    assert "evidence_index" in prompt.messages[0]["content"]
    assert "- chunk:abc" in prompt.messages[0]["content"]
    assert "- chunk:def" in prompt.messages[0]["content"]
    # The incident record itself is context, not evidence: it must NOT be referenceable.
    assert "evidence_index" in prompt.messages[0]["content"]


def test_incident_evidence_index_only_contains_chunk_ids():
    request = make_request(
        retrieved_documents=[
            {"id": "chunk:abc", "document_type": "Incident", "content": "x"},
            {"id": "chunk:def", "document_type": "Runbook", "content": "y"},
        ]
    )
    assert build_incident_evidence_index(request) == ["chunk:abc", "chunk:def"]


def test_symbol_like_terms_extracts_camel_case_identifiers():
    terms = _symbol_like_terms(
        ["JwtSecurityTokenHandler: IDX10503 signature validation failed", "401 from PaymentGatewayClient"]
    )
    assert "JwtSecurityTokenHandler" in terms
    assert "PaymentGatewayClient" in terms
    assert "401" not in terms  # digits belong to the keyword leg, not the symbol leg
    assert "JWT" not in terms  # acronyms belong to the keyword leg


# --- mock provider (deterministic, grounded by construction) ---


@pytest.mark.asyncio
async def test_incident_mock_provider_valid_first_call():
    provider = MockAIProvider()
    service = AnalysisService(provider=provider, settings=make_settings())
    response = await service.analyze_incident(
        make_request(
            retrieved_documents=[
                {"id": "chunk:abc", "document_type": "Runbook", "content": "authentication-failure runbook"}
            ]
        )
    )

    assert response.analysis_type == "incident"
    assert response.usage.validation_status == "valid"
    assert response.usage.repair_attempts == 0
    assert response.result.root_cause_candidates
    candidate = response.result.root_cause_candidates[0]
    assert 0.0 <= candidate.confidence <= 1.0
    assert candidate.evidence_ids  # grounded by construction
    assert response.result.remediation.insufficient_evidence is False


@pytest.mark.asyncio
async def test_incident_mock_without_evidence_marks_insufficient():
    """No retrieved chunks -> no candidates, remediation signals insufficient evidence
    (honesty rule, brief §18/§19)."""
    provider = MockAIProvider()
    service = AnalysisService(provider=provider, settings=make_settings())
    # ai_auto_retrieve is off in unit tests and no package was supplied -> zero chunks.
    empty = await service.analyze_incident(make_request())
    assert empty.usage.validation_status == "valid"
    assert empty.result.root_cause_candidates == []
    assert empty.result.remediation.insufficient_evidence is True
    assert empty.result.evidence == []


# --- scripted provider: repair, grounding, provider errors ---


@pytest.mark.asyncio
async def test_incident_grounding_violation_triggers_repair():
    ungrounded = grounded_incident_result()
    ungrounded.root_cause_candidates[0].evidence_ids = ["chunk:invented"]
    provider = ScriptedProvider(ok_incident_result(), ok_incident_result())
    provider._queue = [
        StructuredResult(
            content=ungrounded.model_dump_json(),
            parsed=ungrounded,
            usage=ProviderUsage(),
            latency_ms=10,
            model="scripted-model",
        ),
        ok_incident_result(),
    ]
    service = AnalysisService(provider=provider, settings=make_settings())
    response = await service.analyze_incident(
        make_request(
            retrieved_documents=[
                {"id": "chunk:abc", "document_type": "Runbook", "content": "authentication-failure runbook"}
            ]
        )
    )
    assert response.usage.validation_status == "repaired"
    repair_turn = provider.calls[1]["messages"][-1]["content"]
    assert "evidence index" in repair_turn


@pytest.mark.asyncio
async def test_incident_safe_failure_after_bounded_repair():
    bad = StructuredResult(content='{"rootCauseCandidates": ', parsed=None, usage=ProviderUsage())
    provider = ScriptedProvider(bad, bad, bad)
    service = AnalysisService(provider=provider, settings=make_settings(ai_max_repair_attempts=2))
    with pytest.raises(AiValidationError) as exc_info:
        await service.analyze_incident(make_request())
    assert exc_info.value.details["attempts"] == 3


@pytest.mark.asyncio
async def test_incident_provider_rate_limited_maps_to_429():
    provider = ScriptedProvider(ProviderRateLimited("limit"))
    service = AnalysisService(provider=provider, settings=make_settings())
    with pytest.raises(AiRateLimitedError):
        await service.analyze_incident(make_request())


@pytest.mark.asyncio
async def test_incident_provider_timeout_maps_to_504():
    provider = ScriptedProvider(ProviderTimeout("slow"))
    service = AnalysisService(provider=provider, settings=make_settings())
    with pytest.raises(AiTimeoutError):
        await service.analyze_incident(make_request())


@pytest.mark.asyncio
async def test_incident_provider_unavailable_maps_to_502():
    provider = ScriptedProvider(ProviderUnavailable("down"))
    service = AnalysisService(provider=provider, settings=make_settings())
    with pytest.raises(AiProviderError):
        await service.analyze_incident(make_request())
