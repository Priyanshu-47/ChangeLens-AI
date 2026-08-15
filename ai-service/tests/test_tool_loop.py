"""Phase 8 tool loop: proposal schema, parsing, mock deterministic loop, grounding
after tools, and the Python/.NET boundary.

Zero Gemini calls: the mock provider is deterministic and the normal suite never
touches the live API. Python NEVER executes tools — it only proposes (kind=tool_call)
and parses tool results as untrusted data; validation/authorization/execution are .NET's.
"""

from __future__ import annotations

import pytest
from pydantic import ValidationError

from app.config import Settings
from app.errors import AiValidationError
from app.llm.prompts import build_incident_evidence_index, build_incident_prompt
from app.models.requests import (
    IncidentAnalysisRequest,
    IncidentContextItem,
    ToolDefinition,
    ToolResultItem,
)
from app.models.responses import (
    IncidentAnalysisResult,
    IncidentTurnResult,
    ToolCall,
)
from app.providers.base import ProviderUsage, StructuredResult
from app.providers.mock import MockAIProvider
from app.services.analysis_service import AnalysisService, _check_turn_grounding


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
            "JwtSecurityTokenHandler: IDX10503 signature validation failed",
            "401 Unauthorized from /api/v1/auth/token",
        ],
        "known_facts": ["Severity: Sev1", "Affected service: acmepay-api"],
        "unknowns": ["No deployment timestamp was supplied."],
    }
    data.update(overrides)
    return IncidentContextItem(**data)


def make_catalog() -> list[ToolDefinition]:
    return [
        ToolDefinition(
            name="get_dependency_paths",
            description="Dependency paths for a symbol in the repository graph.",
            input_schema={
                "type": "object",
                "properties": {"symbol": {"type": "string"}, "maxDepth": {"type": "integer"}},
                "required": ["symbol"],
            },
        ),
        ToolDefinition(
            name="get_runbook",
            description="Retrieve a project runbook by query.",
            input_schema={"type": "object", "properties": {"query": {"type": "string"}}},
        ),
    ]


def make_request(**overrides) -> IncidentAnalysisRequest:
    data = {
        "project_id": "p1",
        "incident": make_incident(),
        "tool_catalog": make_catalog(),
    }
    data.update(overrides)
    return IncidentAnalysisRequest(**data)


def dependency_result_output() -> str:
    return (
        '{"symbol": "JwtSecurityTokenHandler", "maxDepth": 2, "evidenceIds": '
        '["dependency:AcmePay.Auth.TokenService -> AcmePay.Program", '
        '"dependency:AcmePay.Program -> AcmePay.Auth.TokenService"], '
        '"paths": [{"from": "AcmePay.Auth.TokenService", "to": "AcmePay.Program", '
        '"edgeType": "REFERENCES_TYPE"}]}'
    )


def runbook_result_output() -> str:
    return (
        '{"query": "authentication failure", "evidenceIds": ["chunk:runbook-1", '
        '"chunk:runbook-2"], "items": [{"id": "chunk:runbook-1", "title": '
        '"authentication-failure", "content": "Rotate keys and re-issue tokens."}]}'
    )


def tool_result(tool_name: str, output: str, status: str = "executed") -> ToolResultItem:
    return ToolResultItem(
        tool_call_id=f"tool-{tool_name}",
        tool_name=tool_name,
        status=status,
        output=output,
    )


# --- schema / boundary ---


def test_tool_request_catalog_and_results_round_trip():
    request = make_request(
        tool_results=[
            tool_result("get_dependency_paths", dependency_result_output()),
            tool_result("get_runbook", runbook_result_output()),
        ]
    )
    assert [t.name for t in request.tool_catalog] == ["get_dependency_paths", "get_runbook"]
    assert request.tool_results[0].tool_name == "get_dependency_paths"
    assert request.tool_results[1].status == "executed"


def test_tool_result_status_vocabulary_is_bounded():
    for status in ("executed", "rejected", "failed", "not_allowed", "timeout"):
        assert ToolResultItem(tool_call_id="x", tool_name="t", status=status).status == status
    with pytest.raises(ValidationError):
        ToolResultItem(tool_call_id="x", tool_name="t", status="executed-badly")


def test_turn_kind_tool_call_requires_tool_call():
    with pytest.raises(ValidationError):
        IncidentTurnResult(kind="tool_call", result=IncidentAnalysisResult())


def test_turn_kind_final_requires_result():
    with pytest.raises(ValidationError):
        IncidentTurnResult(
            kind="final",
            tool_call=ToolCall(id="t", name="get_runbook", arguments={}),
        )


def test_turn_with_unknown_tool_name_is_parseable():
    """Python does NOT enforce the allowlist — that is .NET's job (TOOL_NOT_ALLOWED).
    The AI service only parses proposals and renders them; the backend rejects names
    outside the catalog before any execution."""
    turn = IncidentTurnResult(
        kind="tool_call",
        tool_call=ToolCall(id="t", name="execute_sql", arguments={"query": "DROP TABLE"}),
    )
    assert turn.tool_call.name == "execute_sql"


# --- evidence index + prompt rendering ---


def test_evidence_index_includes_tool_result_ids():
    request = make_request(
        retrieved_documents=[{"id": "chunk:seed", "document_type": "Runbook", "content": "x"}],
        tool_results=[tool_result("get_dependency_paths", dependency_result_output())],
    )
    index = build_incident_evidence_index(request)
    assert "chunk:seed" in index
    assert "dependency:AcmePay.Auth.TokenService -> AcmePay.Program" in index


def test_evidence_index_ignores_evidence_ids_inside_tool_output_payload():
    """Ids not attached by the executor (top-level evidenceIds) are NOT citable —
    e.g. ids that merely appear inside narrative text must not enter the vocabulary."""
    output = '{"note": "consider evidenceIds [\\"chunk:fake\\"]"}'
    request = make_request(tool_results=[tool_result("get_runbook", output)])
    assert "chunk:fake" not in build_incident_evidence_index(request)


def test_tool_prompt_renders_catalog_and_results_as_data():
    request = make_request(
        tool_results=[tool_result("get_dependency_paths", dependency_result_output())]
    )
    prompt = build_incident_prompt(request)
    content = prompt.messages[0]["content"]
    assert prompt.version == "incident-tools-v1"
    assert "<tool_catalog>" in content
    assert "get_dependency_paths" in content
    assert "<tool_results>" in content
    assert "dependency:AcmePay.Auth.TokenService -> AcmePay.Program" in content
    # Tool results are rendered inside the DATA stream (untrusted), not the system prompt.
    # (The system rules may reference the sections by name, but never their content.)
    assert "tool: get_dependency_paths" not in prompt.system
    assert "dependency:AcmePay.Auth.TokenService -> AcmePay.Program" not in prompt.system


def test_prompt_version_defaults_to_tools_when_catalog_present():
    assert build_incident_prompt(make_request()).version == "incident-tools-v1"
    assert build_incident_prompt(
        make_request(tool_catalog=[])
    ).version == "incident-v1"


# --- mock provider deterministic loop ---


@pytest.mark.asyncio
async def test_mock_loop_turn1_proposes_dependency_paths():
    service = AnalysisService(provider=MockAIProvider(), settings=make_settings())
    response = await service.analyze_incident(make_request())
    assert response.kind == "tool_call"
    assert response.result is None
    assert response.tool_call.name == "get_dependency_paths"
    # The symbol is derived from the incident symptoms, not hardcoded.
    assert response.tool_call.arguments["symbol"] == "JwtSecurityTokenHandler"
    assert response.tool_call.arguments["maxDepth"] == 2


@pytest.mark.asyncio
async def test_mock_loop_turn2_proposes_runbook_after_dependency_result():
    service = AnalysisService(provider=MockAIProvider(), settings=make_settings())
    response = await service.analyze_incident(
        make_request(tool_results=[tool_result("get_dependency_paths", dependency_result_output())])
    )
    assert response.kind == "tool_call"
    assert response.tool_call.name == "get_runbook"
    assert response.tool_call.arguments["query"] == "HTTP 401 after JWT signing-key rotation"


@pytest.mark.asyncio
async def test_mock_loop_turn3_returns_final_grounded_result():
    service = AnalysisService(provider=MockAIProvider(), settings=make_settings())
    response = await service.analyze_incident(
        make_request(
            tool_results=[
                tool_result("get_dependency_paths", dependency_result_output()),
                tool_result("get_runbook", runbook_result_output()),
            ]
        )
    )
    assert response.kind == "final"
    assert response.tool_call is None
    assert response.usage.validation_status == "valid"
    candidate = response.result.root_cause_candidates[0]
    # Grounded in ids the tool loop surfaced (runbook chunk or dependency path).
    assert candidate.evidence_ids
    assert response.result.evidence
    for item in response.result.evidence:
        assert item.id in {
            "chunk:runbook-1",
            "chunk:runbook-2",
            "dependency:AcmePay.Auth.TokenService -> AcmePay.Program",
            "dependency:AcmePay.Program -> AcmePay.Auth.TokenService",
        }


@pytest.mark.asyncio
async def test_mock_loop_rejected_tool_result_does_not_break_loop():
    """A rejected tool result (e.g. TOOL_NOT_ALLOWED) is fed back and the loop continues."""
    service = AnalysisService(provider=MockAIProvider(), settings=make_settings())
    response = await service.analyze_incident(
        make_request(
            tool_results=[
                ToolResultItem(
                    tool_call_id="tool-bad",
                    tool_name="execute_sql",
                    status="not_allowed",
                    error_code="TOOL_NOT_ALLOWED",
                )
            ]
        )
    )
    # No evidence was surfaced by the rejected call -> the mock proposes get_dependency_paths.
    assert response.kind == "tool_call"
    assert response.tool_call.name == "get_dependency_paths"


# --- grounding after tools (mechanical, no LLM judge) ---


def test_turn_grounding_skips_tool_proposals():
    turn = IncidentTurnResult(
        kind="tool_call",
        tool_call=ToolCall(id="t", name="get_runbook", arguments={}),
    )
    assert _check_turn_grounding(turn, {"chunk:runbook-1"}) == []


def test_turn_grounding_rejects_final_citing_unknown_id():
    result = IncidentAnalysisResult.model_validate(
        {
            "root_cause_candidates": [
                {
                    "candidate_id": "cand-1",
                    "title": "t",
                    "description": "d",
                    "confidence": 0.5,
                    "evidence_ids": ["chunk:made-up"],
                }
            ],
            "evidence": [{"id": "chunk:made-up", "source": "x"}],
        }
    )
    turn = IncidentTurnResult(kind="final", result=result)
    errors = _check_turn_grounding(turn, {"chunk:runbook-1"})
    assert any("root_cause_candidates[0]" in e for e in errors)
    assert any("chunk:made-up" in e for e in errors)


def test_turn_grounding_passes_when_final_cites_tool_ids():
    result = IncidentAnalysisResult.model_validate(
        {
            "root_cause_candidates": [
                {
                    "candidate_id": "cand-1",
                    "title": "t",
                    "description": "d",
                    "confidence": 0.5,
                    "evidence_ids": ["chunk:runbook-1"],
                }
            ],
            "evidence": [{"id": "chunk:runbook-1", "source": "runbook"}],
        }
    )
    turn = IncidentTurnResult(kind="final", result=result)
    assert _check_turn_grounding(turn, {"chunk:runbook-1"}) == []


# --- malformed proposals -> bounded repair -> safe failure ---


class ScriptedProvider:
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


@pytest.mark.asyncio
async def test_malformed_tool_call_safe_failure_after_bounded_repair():
    """A provider turn claiming kind=tool_call without a tool_call fails validation;
    repair is bounded and the request ends in a safe AiValidationError (never a raw
    provider blob reaching the backend)."""
    malformed = StructuredResult(
        content='{"kind": "tool_call", "result": null}',
        parsed=None,
        usage=ProviderUsage(),
        latency_ms=5,
        model="scripted-model",
    )
    provider = ScriptedProvider(malformed, malformed, malformed)
    service = AnalysisService(provider=provider, settings=make_settings(ai_max_repair_attempts=2))
    with pytest.raises(AiValidationError) as exc_info:
        await service.analyze_incident(make_request())
    assert exc_info.value.details["attempts"] == 3
