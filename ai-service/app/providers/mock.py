"""Deterministic mock provider for local development, unit tests, and the
ASP.NET -> FastAPI integration path without Gemini (brief §23).

The mock is *grounded by construction*: it builds its risk factors from the evidence
ids present in the rendered prompt's evidence index, so the grounding validation in the
analysis service passes. It never fabricates token usage — tokens stay None; latency is
wall-clock measured.
"""

from __future__ import annotations

import logging
import re
import time

from pydantic import BaseModel

from ..models.responses import (
    AnalysisUsage,  # noqa: F401 (re-exported for parity with provider surface)
    EvidenceItem,
    EvidenceReference,
    EvidenceType,
    ImpactedComponent,
    ImpactType,
    RecommendedTest,
    RiskAnalysisResult,
    RiskFactor,
    RiskLevel,
    TestCategory,
)
from .base import ProviderUsage, StructuredResult

logger = logging.getLogger(__name__)

# Evidence-index lines are `- <id>`. Dependency ids contain ` -> `, so capture the
# optional arrow segment as part of the id.
_EVIDENCE_ID_RE = re.compile(r"^-\s+([a-z]+:[^\s]+(?:\s+->\s+[^\s]+)?)\s*$", re.MULTILINE)


class MockAIProvider:
    """Deterministic structured-output provider. Select with AI_PROVIDER=mock."""

    provider_name = "mock"

    def __init__(self, *, model: str = "mock-gemini-3.7-flash", latency_ms: int | None = None):
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
