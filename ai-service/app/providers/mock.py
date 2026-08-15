"""Deterministic mock provider for local development, unit tests, and the
ASP.NET -> FastAPI integration path without Gemini (brief §23).

The mock is *grounded by construction*: it builds its risk factors from the evidence
ids present in the rendered prompt's evidence index, so the grounding validation in the
analysis service passes. It never fabricates token usage — tokens stay None; latency is
wall-clock measured.
"""

from __future__ import annotations

import json
import logging
import re
import time

from pydantic import BaseModel

from ..models.responses import (
    AnalysisUsage,  # noqa: F401 (re-exported for parity with provider surface)
    CandidateStatus,
    EvidenceItem,
    EvidenceReference,
    EvidenceType,
    ImpactedComponent,
    ImpactType,
    IncidentAnalysisResult,
    IncidentEvidenceItem,
    IncidentTurnResult,
    RecommendedTest,
    Remediation,
    RiskAnalysisResult,
    RiskFactor,
    RiskLevel,
    RootCauseCandidate,
    TestCategory,
    ToolCall,
)
from .base import ProviderUsage, StructuredResult

logger = logging.getLogger(__name__)

# Evidence-index lines are `- <id>`. Dependency ids contain ` -> `, so capture the
# optional arrow segment as part of the id.
_EVIDENCE_ID_RE = re.compile(r"^-\s+([a-z]+:[^\s]+(?:\s+->\s+[^\s]+)?)\s*$", re.MULTILINE)

# Symbol-like identifiers in the incident text (mirrors analysis_service; the mock
# derives its deterministic get_dependency_paths proposal from real request content).
# Require an inner capital so prose words like "The" are not mistaken for symbols.
_SYMBOL_RE = re.compile(r"\b[A-Z][a-z]+[A-Z][A-Za-z0-9]*\b")

# Incident title line rendered by the prompt builder (<incident> title: ...).
_TITLE_RE = re.compile(r"^title:\s*(.+)$", re.MULTILINE)

_TOOL_RESULTS_SECTION_RE = re.compile(r"<tool_results>(.*?)</tool_results>", re.DOTALL)
_TOOL_RESULT_BLOCK_RE = re.compile(
    r'<tool_result[^>]*tool="([^"]+)"[^>]*>(.*?)</tool_result>', re.DOTALL
)


def _tool_result_evidence_ids(user_content: str) -> list[str]:
    """Evidence ids attached by the tool executor, parsed from the rendered results.

    The mock mirrors the grounding contract: only ids the executor declared in the
    output's top-level `evidenceIds` array are citable — everything else is data.
    """
    section = _TOOL_RESULTS_SECTION_RE.search(user_content)
    if not section:
        return []
    ids: list[str] = []
    for _, body in _TOOL_RESULT_BLOCK_RE.findall(section.group(1)):
        try:
            data = json.loads(body)
        except (ValueError, TypeError):
            continue
        if isinstance(data, dict) and isinstance(data.get("evidenceIds"), list):
            for eid in data["evidenceIds"]:
                if isinstance(eid, str) and eid and eid not in ids:
                    ids.append(eid)
    return ids


class MockAIProvider:
    """Deterministic structured-output provider. Select with AI_PROVIDER=mock."""

    provider_name = "mock"

    def __init__(self, *, model: str = "mock-gemini-3.1-flash-lite", latency_ms: int | None = None):
        self._model = model
        self._fixed_latency_ms = latency_ms

    @property
    def model(self) -> str:
        return self._model

    async def complete_structured(
        self,
        *,
        system: str,
        messages: list[dict[str, str]],
        response_schema: type[BaseModel],
        prompt_version: str | None = None,
    ) -> StructuredResult:
        started = time.perf_counter()
        user_content = "\n".join(m["content"] for m in messages if m.get("role") == "user")
        evidence_ids = _EVIDENCE_ID_RE.findall(user_content)
        if response_schema is IncidentTurnResult:
            turn = self._build_incident_turn(evidence_ids, user_content)
            content = turn.model_dump_json(indent=2)
            latency_ms = self._fixed_latency_ms or max(
                1, int((time.perf_counter() - started) * 1000)
            )
            return StructuredResult(
                content=content,
                parsed=turn,
                usage=ProviderUsage(),
                latency_ms=latency_ms,
                model=self._model,
                finish_reason="STOP",
            )
        if response_schema is IncidentAnalysisResult:
            result = self._build_incident_result(evidence_ids)
        else:
            result = self._build_result(evidence_ids)
        latency_ms = self._fixed_latency_ms or max(1, int((time.perf_counter() - started) * 1000))
        return StructuredResult(
            content=result.model_dump_json(indent=2),
            parsed=result,
            usage=ProviderUsage(),  # mock has no token accounting
            latency_ms=latency_ms,
            model=self._model,
            finish_reason="STOP",
        )

    def _build_incident_turn(self, evidence_ids: list[str], user_content: str) -> IncidentTurnResult:
        """Deterministic tool-loop behavior (docs/agent-tools.md §5, brief §26).

        Turn 1 (no tool results yet): propose `get_dependency_paths` for the first
        symbol-like identifier in the incident text. Turn 2 (dependency ids present,
        no tool-surfaced chunks yet): propose `get_runbook` for the incident title.
        Turn 3 (both): return the final grounded investigation, citing the evidence
        the tool loop surfaced. Fully deterministic — no randomness, and the final
        answer is grounded by construction in ids that exist in the evidence index.
        """
        tool_ids = _tool_result_evidence_ids(user_content)
        chunk_ids = [eid for eid in evidence_ids if eid.startswith("chunk:")]
        tool_chunk_ids = [eid for eid in tool_ids if eid.startswith("chunk:")]

        if not tool_ids:
            symbol = self._first_symbol(user_content)
            return IncidentTurnResult(
                kind="tool_call",
                tool_call=ToolCall(
                    id="tool-1",
                    name="get_dependency_paths",
                    arguments={"symbol": symbol, "maxDepth": 2},
                ),
            )

        if not tool_chunk_ids:
            title = self._incident_title(user_content) or "authentication failure"
            return IncidentTurnResult(
                kind="tool_call",
                tool_call=ToolCall(
                    id="tool-2",
                    name="get_runbook",
                    arguments={"query": title, "topK": 3},
                ),
            )

        # Final turn: grounded in ids the tool loop actually surfaced.
        dependency_ids = [eid for eid in tool_ids if eid.startswith("dependency:")]
        cited = tool_chunk_ids + dependency_ids + chunk_ids
        evidence: list[IncidentEvidenceItem] = []
        candidates: list[RootCauseCandidate] = []

        for eid in cited[:3]:
            evidence.append(
                IncidentEvidenceItem(
                    id=eid,
                    type=EvidenceType.Document,
                    source=eid,
                    summary="Evidence surfaced during the investigation tool loop.",
                )
            )

        if cited:
            candidates.append(
                RootCauseCandidate(
                    candidate_id="cand-1",
                    title="Change-related hypothesis from tool-surfaced evidence",
                    description=(
                        "The dependency paths and runbook evidence gathered during the "
                        "investigation suggest a change-related cause; confirm against "
                        "production telemetry before acting."
                    ),
                    confidence=0.6,
                    status=CandidateStatus.CANDIDATE,
                    evidence_ids=[cited[0]],
                    reasoning=f"Grounded in evidence {cited[0]} gathered by the tool loop.",
                    unknowns=[
                        "No deployment timestamp was supplied.",
                        "No application log sample was supplied.",
                    ],
                )
            )

        return IncidentTurnResult(
            kind="final",
            result=IncidentAnalysisResult(
                root_cause_candidates=candidates,
                remediation=Remediation(
                    immediate_mitigation=(
                        "Confirm the most recent deployment window and check the affected service's health."
                        if cited
                        else None
                    ),
                    investigation_steps=[
                        "Correlate the first error timestamp with the deployment window.",
                        "Review the dependency paths and runbook evidence gathered.",
                    ],
                    recommended_remediation=None,
                    validation_steps=[],
                    rollback_consideration=(
                        "If a change correlates with symptom onset, evaluate rolling it back."
                        if cited
                        else None
                    ),
                    insufficient_evidence=not bool(cited),
                ),
                unknowns=[],
                evidence=evidence,
            ),
        )

    @staticmethod
    def _first_symbol(user_content: str) -> str:
        match = _SYMBOL_RE.search(user_content)
        return match.group(0) if match else "TokenService"

    @staticmethod
    def _incident_title(user_content: str) -> str | None:
        match = _TITLE_RE.search(user_content)
        return match.group(1).strip() if match else None

    def _build_incident_result(self, evidence_ids: list[str]) -> IncidentAnalysisResult:
        """Deterministic, grounded-by-construction incident investigation (brief §39)."""
        chunk_ids = [eid for eid in evidence_ids if eid.startswith("chunk:")]
        evidence: list[IncidentEvidenceItem] = []
        candidates: list[RootCauseCandidate] = []

        for eid in chunk_ids[:3]:
            evidence.append(
                IncidentEvidenceItem(
                    id=eid,
                    type=EvidenceType.Document,
                    source=eid,
                    summary="Retrieved evidence relevant to this incident (hybrid retrieval).",
                )
            )

        if chunk_ids:
            candidates.append(
                RootCauseCandidate(
                    candidate_id="cand-1",
                    title="Change-related hypothesis from retrieved evidence",
                    description=(
                        "The retrieved runbook/incident/source evidence suggests a change-related "
                        "cause; confirm against production telemetry before acting."
                    ),
                    confidence=0.6,
                    status=CandidateStatus.CANDIDATE,
                    evidence_ids=[chunk_ids[0]],
                    reasoning=f"Grounded in retrieved evidence {chunk_ids[0]}.",
                    unknowns=[
                        "No deployment timestamp was supplied.",
                        "No application log sample was supplied.",
                    ],
                )
            )

        return IncidentAnalysisResult(
            root_cause_candidates=candidates,
            remediation=Remediation(
                immediate_mitigation=(
                    "Confirm the most recent deployment window and check the affected service's health."
                    if chunk_ids
                    else None
                ),
                investigation_steps=[
                    "Correlate the first error timestamp with the deployment window.",
                    "Review recent configuration changes to the affected service.",
                ],
                recommended_remediation=None,
                validation_steps=[],
                rollback_consideration=(
                    "If a change correlates with symptom onset, evaluate rolling it back."
                    if chunk_ids
                    else None
                ),
                insufficient_evidence=not bool(chunk_ids),
            ),
            unknowns=[],
            evidence=evidence,
        )

    def _build_result(self, evidence_ids: list[str]) -> RiskAnalysisResult:
        # Grounded by construction: factors reference ids that ARE in the evidence index.
        change_ids = [eid for eid in evidence_ids if eid.startswith("change:")]
        chunk_ids = [eid for eid in evidence_ids if eid.startswith("chunk:")]
        symbol_ids = [eid for eid in evidence_ids if eid.startswith("symbol:")]
        dependency_ids = [eid for eid in evidence_ids if eid.startswith("dependency:")]
        factors: list[RiskFactor] = []
        evidence: list[EvidenceItem] = []
        components: list[ImpactedComponent] = []

        for eid in change_ids:
            path = eid.split(":", 1)[1]
            file_name = path.rsplit("/", 1)[-1]
            evidence.append(
                EvidenceItem(
                    id=eid,
                    type=EvidenceType.ChangedFile,
                    reference=path,
                    summary=f"{file_name} was modified by this change.",
                )
            )
            components.append(
                ImpactedComponent(
                    name=file_name,
                    file_path=path,
                    impact=ImpactType.MODIFIED,
                )
            )
            factors.append(
                RiskFactor(
                    id=f"rf-{len(factors) + 1}",
                    title=f"Changed file {file_name}",
                    description=f"{file_name} is part of this change; review dependent code.",
                    severity=RiskLevel.MEDIUM,
                    evidence=[EvidenceReference(type=EvidenceType.ChangedFile, reference=eid)],
                )
            )

        # Phase 4 change-intelligence evidence: surface the first dependency edge and
        # first changed symbol so the analyzer -> graph -> retrieval chain is observable
        # even with the deterministic mock provider.
        if dependency_ids and factors:
            top_dep = dependency_ids[0]
            evidence.append(
                EvidenceItem(
                    id=top_dep,
                    type=EvidenceType.Dependency,
                    reference=top_dep,
                    summary="Dependency edge proved by the Roslyn analyzer between the changed and an impacted symbol.",
                )
            )
            factors[0].evidence.append(
                EvidenceReference(type=EvidenceType.Dependency, reference=top_dep)
            )
        if symbol_ids and factors:
            top_symbol = symbol_ids[0]
            evidence.append(
                EvidenceItem(
                    id=top_symbol,
                    type=EvidenceType.Component,
                    reference=top_symbol,
                    summary="Changed symbol extracted by the Roslyn analyzer.",
                )
            )
            factors[0].evidence.append(
                EvidenceReference(type=EvidenceType.Component, reference=top_symbol)
            )

        # Retrieved evidence (Phase 3): the top chunk is surfaced as a Document
        # evidence item and referenced by the first factor, so the retrieval -> analysis
        # chain is observable even with the deterministic mock provider.
        if chunk_ids and factors:
            top = chunk_ids[0]
            evidence.append(
                EvidenceItem(
                    id=top,
                    type=EvidenceType.Document,
                    reference=top,
                    summary="Retrieved evidence relevant to this change (hybrid retrieval + dependency leg).",
                )
            )
            factors[0].evidence.append(
                EvidenceReference(type=EvidenceType.Document, reference=top)
            )

        return RiskAnalysisResult(
            risk_level=RiskLevel.HIGH if len(change_ids) > 2 else RiskLevel.MEDIUM,
            confidence=0.7,
            impacted_components=components,
            risk_factors=factors,
            historical_incidents=[],
            recommended_tests=[
                RecommendedTest(
                    category=TestCategory.Regression,
                    target_component="changed files",
                    description="Run regression suite covering the changed files.",
                )
            ],
            unknowns=[],
            evidence=evidence,
        )
