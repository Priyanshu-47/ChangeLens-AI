"""Structured output models for change-risk analysis.

`RiskAnalysisResult` is the single source of truth for the output schema (ADR-0007):
it is passed to the provider as the response schema AND used to validate the response.
The JSON shape mirrors docs/api-contract.md §3 (risk report).
"""

from __future__ import annotations

from enum import Enum
from typing import Literal

from pydantic import Field

from .common import ApiModel


class RiskLevel(str, Enum):
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class ImpactType(str, Enum):
    MODIFIED = "MODIFIED"
    ADDED = "ADDED"
    REMOVED = "REMOVED"
    DEPENDENT = "DEPENDENT"


class EvidenceType(str, Enum):
    ChangedFile = "ChangedFile"
    Component = "Component"
    Dependency = "Dependency"
    ApiContract = "ApiContract"
    HistoricalIncident = "HistoricalIncident"
    Document = "Document"
    Deployment = "Deployment"
    Log = "Log"
    Runbook = "Runbook"


class TestCategory(str, Enum):
    Unit = "Unit"
    Integration = "Integration"
    Regression = "Regression"
    Manual = "Manual"


class ImpactedComponent(ApiModel):
    component_id: str | None = Field(default=None, max_length=200)
    name: str = Field(min_length=1, max_length=300)
    service: str | None = Field(default=None, max_length=300)
    file_path: str | None = Field(default=None, max_length=1000)
    impact: ImpactType = ImpactType.MODIFIED


class EvidenceReference(ApiModel):
    """A single evidence pointer attached to a conclusion.

    `reference` MUST be an id from the evidence index when the model intends to point at
    input evidence (the grounding rule). Human-readable annotations (e.g. file:line) are
    allowed as extra context but never satisfy grounding on their own.
    """

    type: EvidenceType = EvidenceType.Document
    reference: str = Field(min_length=1, max_length=2000)


class RiskFactor(ApiModel):
    id: str | None = Field(default=None, max_length=200)
    title: str = Field(min_length=1, max_length=300)
    description: str = Field(min_length=1, max_length=2000)
    severity: RiskLevel = RiskLevel.MEDIUM
    # Grounding is enforced by the service (deterministic, post-validation): a factor
    # must reference >=1 evidence index id. Non-empty here is the Pydantic half.
    evidence: list[EvidenceReference] = Field(min_length=1, max_length=10)
    unknowns: list[str] = Field(default_factory=list, max_length=10)


class HistoricalIncident(ApiModel):
    incident_id: str | None = Field(default=None, max_length=200)
    reference: str = Field(min_length=1, max_length=300)
    similarity: float | None = Field(default=None, ge=0.0, le=1.0)
    summary: str | None = Field(default=None, max_length=1000)
    evidence: str | None = Field(default=None, max_length=500)


class RecommendedTest(ApiModel):
    category: TestCategory = TestCategory.Regression
    target_component: str | None = Field(default=None, max_length=300)
    description: str = Field(min_length=1, max_length=1000)


class EvidenceItem(ApiModel):
    id: str = Field(min_length=1, max_length=500)
    type: EvidenceType
    reference: str = Field(min_length=1, max_length=2000)
    summary: str | None = Field(default=None, max_length=2000)
    ai_document_id: str | None = Field(default=None, max_length=500)


class RiskAnalysisResult(ApiModel):
    """Validated structured risk report. Never returned unvalidated (ADR-0007)."""

    risk_level: RiskLevel
    confidence: float = Field(ge=0.0, le=1.0)
    impacted_components: list[ImpactedComponent] = Field(default_factory=list, max_length=50)
    risk_factors: list[RiskFactor] = Field(default_factory=list, max_length=25)
    historical_incidents: list[HistoricalIncident] = Field(default_factory=list, max_length=25)
    recommended_tests: list[RecommendedTest] = Field(default_factory=list, max_length=25)
    unknowns: list[str] = Field(default_factory=list, max_length=25)
    evidence: list[EvidenceItem] = Field(default_factory=list, max_length=50)


class AnalysisUsage(ApiModel):
    """Observed run metadata for the backend to persist in analysis_runs.

    Nothing here is fabricated: token counts come from provider usage metadata when
    available, cost is computed from configured pricing only, and nulls mean "unknown".
    """

    model: str | None = None
    prompt_version: str | None = None
    latency_ms: int | None = None
    input_tokens: int | None = None
    output_tokens: int | None = None
    total_tokens: int | None = None
    estimated_cost_usd: float | None = None
    validation_status: Literal["valid", "repaired", "failed"] = "valid"
    repair_attempts: int = 0
    evidence_truncated: bool = False


class RiskAnalysisResponse(ApiModel):
    analysis_type: Literal["change-risk"] = "change-risk"
    result: RiskAnalysisResult
    usage: AnalysisUsage


# --- retrieval / ingestion ---


class RetrievalResultSources(ApiModel):
    """Why a result was selected: semantic similarity, keyword rank, and/or dependency
    rank (RRF over all active legs is final; see docs/rag-architecture.md §5)."""

    vector: float | None = None
    keyword: int | None = None
    dependency: int | None = None


class RetrievalResultItem(ApiModel):
    chunk_id: str
    document_id: str
    document_type: str
    chunk_type: str | None = None
    source: str | None = None
    content: str
    metadata: dict[str, object] = Field(default_factory=dict)
    score: float
    sources: RetrievalResultSources = Field(default_factory=RetrievalResultSources)


class RetrievalUsage(ApiModel):
    queries: list[str] = Field(default_factory=list)
    latency_ms: int | None = None
    tokens: dict[str, int] = Field(default_factory=dict)
    strategy: str = "hybrid"


class RetrievalSearchResponse(ApiModel):
    results: list[RetrievalResultItem] = Field(default_factory=list)
    usage: RetrievalUsage = Field(default_factory=RetrievalUsage)


class IngestResponse(ApiModel):
    document_ids: list[str] = Field(default_factory=list)
    chunk_count: int = 0
    skipped: int = 0
    errors: list[dict[str, object]] = Field(default_factory=list)
