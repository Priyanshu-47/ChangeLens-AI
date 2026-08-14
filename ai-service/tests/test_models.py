"""Structured-output model validation: enums, bounds, grounding rule."""

import pytest
from pydantic import ValidationError

from app.models.requests import RiskAnalysisRequest
from app.models.responses import (
    EvidenceItem,
    EvidenceReference,
    EvidenceType,
    RiskAnalysisResult,
    RiskFactor,
    RiskLevel,
)
from app.services.analysis_service import _check_grounding

EVIDENCE_IDS = {"change:src/AuthClient.cs"}


def make_factor(*, evidence_refs, **overrides):
    return RiskFactor(
        title="Auth client changed",
        description="Token refresh logic changed.",
        severity=RiskLevel.MEDIUM,
        evidence=evidence_refs,
        **overrides,
    )


def valid_result(**overrides):
    data = {
        "risk_level": "MEDIUM",
        "confidence": 0.7,
        "impacted_components": [],
        "risk_factors": [
            make_factor(
                evidence_refs=[
                    EvidenceReference(type=EvidenceType.ChangedFile, reference="change:src/AuthClient.cs")
                ]
            )
        ],
        "historical_incidents": [],
        "recommended_tests": [],
        "unknowns": [],
        "evidence": [
            EvidenceItem(
                id="change:src/AuthClient.cs",
                type=EvidenceType.ChangedFile,
                reference="src/AuthClient.cs",
            )
        ],
    }
    data.update(overrides)
    return RiskAnalysisResult(**data)


# --- structural validation ---


def test_confidence_must_be_within_unit_interval():
    with pytest.raises(ValidationError):
        valid_result(confidence=1.5)
    with pytest.raises(ValidationError):
        valid_result(confidence=-0.1)


def test_invalid_risk_level_rejected():
    with pytest.raises(ValidationError):
        valid_result(risk_level="URGENT")


def test_risk_factor_without_evidence_rejected_by_pydantic():
    """The Pydantic half of grounding: evidence is a required, non-empty list."""
    with pytest.raises(ValidationError, match="evidence"):
        make_factor(evidence_refs=[])


def test_camel_case_input_accepted():
    result = RiskAnalysisResult.model_validate(
        {
            "riskLevel": "HIGH",
            "confidence": 0.9,
            "riskFactors": [
                {
                    "title": "x",
                    "description": "y",
                    "severity": "HIGH",
                    "evidence": [
                        {"type": "ChangedFile", "reference": "change:src/AuthClient.cs"}
                    ],
                }
            ],
            "evidence": [
                {
                    "id": "change:src/AuthClient.cs",
                    "type": "ChangedFile",
                    "reference": "src/AuthClient.cs",
                }
            ],
        }
    )
    assert result.risk_level == RiskLevel.HIGH


def test_array_bounds_enforced():
    with pytest.raises(ValidationError):
        valid_result(risk_factors=[make_factor(evidence_refs=[]) for _ in range(30)])


def test_camel_case_output_serialization():
    result = valid_result()
    payload = result.model_dump(mode="json", by_alias=True)
    assert "riskLevel" in payload
    assert "riskFactors" in payload
    assert "impactedComponents" in payload


# --- grounding rule (deterministic post-validation) ---


def test_grounding_ok_when_factor_references_index_id():
    result = valid_result()
    assert _check_grounding(result, EVIDENCE_IDS) == []


def test_grounding_fails_when_factor_references_nothing_from_index():
    result = valid_result(
        risk_factors=[
            make_factor(
                evidence_refs=[
                    EvidenceReference(type=EvidenceType.Document, reference="src/other.cs#L1")
                ]
            )
        ]
    )
    errors = _check_grounding(result, EVIDENCE_IDS)
    assert any("evidence index" in e for e in errors)


def test_grounding_fails_when_evidence_item_invented():
    result = valid_result(
        evidence=[
            EvidenceItem(
                id="change:NOT_IN_PACKAGE.cs",
                type=EvidenceType.ChangedFile,
                reference="NOT_IN_PACKAGE.cs",
            )
        ]
    )
    errors = _check_grounding(result, EVIDENCE_IDS)
    assert any("not in the evidence index" in e for e in errors)


def test_grounding_ok_with_empty_factors_and_evidence():
    assert _check_grounding(valid_result(risk_factors=[], evidence=[]), EVIDENCE_IDS) == []


# --- request validation ---


def test_request_requires_at_least_one_changed_file():
    with pytest.raises(ValidationError, match="changed_files"):
        RiskAnalysisRequest(project_id="p1", change_summary="x", changed_files=[])


def test_request_rejects_huge_summary():
    with pytest.raises(ValidationError):
        RiskAnalysisRequest(project_id="p1", change_summary="x" * 6000, changed_files=[{"path": "a.cs"}])
