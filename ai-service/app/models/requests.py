"""Request models for the internal analysis endpoints.

Phase 2: the AI service receives the evidence package assembled by the backend directly
in the request (no retrieval yet — that is Phase 3). All content below is UNTRUSTED data;
the prompt builder marks it as such and the model is instructed never to act on it.
"""

from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import Field

from .common import ApiModel


class ChangedFile(ApiModel):
    path: str = Field(min_length=1, max_length=1000)
    change_type: Literal["added", "modified", "deleted", "renamed"] = "modified"
    language: str | None = Field(default=None, max_length=50)
    symbols_changed: list[str] = Field(default_factory=list, max_length=200)
    diff_preview: str | None = Field(default=None, max_length=20_000)
    content: str | None = Field(default=None, max_length=200_000)


class ImpactedComponentItem(ApiModel):
    id: str = Field(min_length=1, max_length=200)
    name: str = Field(min_length=1, max_length=300)
    service: str | None = Field(default=None, max_length=300)
    file_path: str | None = Field(default=None, max_length=1000)
    impact: str = Field(default="MODIFIED", max_length=50)


class ApiContractItem(ApiModel):
    id: str = Field(min_length=1, max_length=200)
    service: str = Field(min_length=1, max_length=300)
    path: str = Field(min_length=1, max_length=1000)
    method: str = Field(min_length=1, max_length=20)
    operation_id: str | None = Field(default=None, max_length=300)
    description: str | None = Field(default=None, max_length=2000)


class RetrievedDocumentItem(ApiModel):
    id: str = Field(min_length=1, max_length=200)
    document_type: str = Field(min_length=1, max_length=50)
    title: str | None = Field(default=None, max_length=500)
    content: str = Field(min_length=1, max_length=200_000)
    metadata: dict[str, object] = Field(default_factory=dict)
    score: float | None = Field(default=None, ge=0.0, le=1.0)


class HistoricalIncidentItem(ApiModel):
    incident_id: str = Field(min_length=1, max_length=200)
    reference: str | None = Field(default=None, max_length=300)
    summary: str | None = Field(default=None, max_length=2000)


class RunbookItem(ApiModel):
    id: str = Field(min_length=1, max_length=200)
    title: str = Field(min_length=1, max_length=500)
    content: str = Field(min_length=1, max_length=200_000)


class IngestDocumentItem(ApiModel):
    """One document to ingest (docs/ai-service-boundary.md §3 — id is backend-provided)."""

    id: str = Field(min_length=1, max_length=200)
    document_type: Literal["SourceCode", "OpenApi", "Incident", "Runbook", "DeploymentRecord"] = "Runbook"
    repository_id: str | None = Field(default=None, max_length=200)
    service_id: str | None = Field(default=None, max_length=200)
    incident_id: str | None = Field(default=None, max_length=200)
    file_path: str | None = Field(default=None, max_length=1000)
    language: str | None = Field(default=None, max_length=50)
    environment: str | None = Field(default=None, max_length=100)
    title: str | None = Field(default=None, max_length=500)
    content: str = Field(min_length=1, max_length=2_000_000)
    content_hash: str | None = Field(default=None, max_length=128)  # advisory only


class IngestDocumentsRequest(ApiModel):
    project_id: str = Field(min_length=1, max_length=100)
    documents: list[IngestDocumentItem] = Field(min_length=1, max_length=100)
    reindex: bool = False


class SearchFilters(ApiModel):
    service_id: str | None = Field(default=None, max_length=300)
    language: str | None = Field(default=None, max_length=50)
    environment: str | None = Field(default=None, max_length=100)


class DependencyRetrieval(ApiModel):
    """Dependency-linked retrieval filter (docs/rag-architecture.md §5).

    Chunks whose `path`, `symbol`, or `service` exactly match any term are treated as a
    third ranked list inside RRF (a different kind of evidence, never added to vector
    scores). The backend derives terms from the Roslyn change analysis.
    """

    symbols: list[str] = Field(default_factory=list, max_length=500)
    paths: list[str] = Field(default_factory=list, max_length=500)
    services: list[str] = Field(default_factory=list, max_length=200)


class RetrievalSearchRequest(ApiModel):
    """Hybrid retrieval request (docs/ai-service-boundary.md §3)."""

    project_id: str = Field(min_length=1, max_length=100)
    query: str = Field(min_length=1, max_length=2000)
    document_types: list[str] | None = Field(default=None, max_length=10)
    filters: SearchFilters = Field(default_factory=SearchFilters)
    # "dependency" is used by the evaluation runner's ablation mode (docs/evaluation.md):
    # the dependency leg alone, ranked by its stable chunk-id order.
    strategy: Literal["hybrid", "vector", "keyword", "dependency"] = "hybrid"
    k: int = Field(default=10, ge=1, le=100)
    embedding_model: str | None = Field(default=None, max_length=200)
    dependency: DependencyRetrieval | None = Field(default=None)


class ChangedSymbolItem(ApiModel):
    """One normalized symbol from the Roslyn analyzer (docs/rag-architecture.md §8).

    IDs are stable and become evidence ids (`symbol:<symbol_id>`) in the grounding index.
    """

    symbol_id: str = Field(min_length=1, max_length=1000)
    kind: str = Field(min_length=1, max_length=50)
    name: str = Field(min_length=1, max_length=300)
    fully_qualified_name: str = Field(min_length=1, max_length=1000)
    file_path: str | None = Field(default=None, max_length=1000)
    namespace: str | None = Field(default=None, max_length=300)
    project: str | None = Field(default=None, max_length=300)
    signature: str | None = Field(default=None, max_length=4000)
    return_type: str | None = Field(default=None, max_length=300)
    parameters: list[str] = Field(default_factory=list, max_length=50)


class DependencyEdgeItem(ApiModel):
    """One Roslyn-proven dependency edge (docs/rag-architecture.md §9)."""

    from_symbol_id: str = Field(min_length=1, max_length=1000)
    to_symbol_id: str = Field(min_length=1, max_length=1000)
    edge_type: str = Field(min_length=1, max_length=50)  # CALLS / REFERENCES_TYPE / IMPLEMENTS / INHERITS
    file_path: str | None = Field(default=None, max_length=1000)


class TimelineEventItem(ApiModel):
    """One normalized incident timeline entry (backend-normalized, brief §11)."""

    occurred_at_utc: datetime | None = None
    type: str = Field(min_length=1, max_length=50)
    source: str | None = Field(default=None, max_length=500)
    message: str | None = Field(default=None, max_length=2000)
    raw_data: str | None = Field(default=None, max_length=4000)


class IncidentContextItem(ApiModel):
    """Normalized incident investigation context (brief §12).

    Built by the backend from the domain Incident — no arbitrary DB objects are dumped
    into the prompt. Missing data is represented as explicit unknowns, never fabricated.
    """

    title: str = Field(min_length=1, max_length=500)
    summary: str | None = Field(default=None, max_length=2000)
    severity: str = Field(min_length=1, max_length=20)
    status: str = Field(min_length=1, max_length=20)
    environment: str | None = Field(default=None, max_length=100)
    service: str | None = Field(default=None, max_length=300)
    started_at_utc: datetime | None = None
    detected_at_utc: datetime | None = None
    timeline: list[TimelineEventItem] = Field(default_factory=list, max_length=200)
    symptoms: list[str] = Field(default_factory=list, max_length=20)
    known_facts: list[str] = Field(default_factory=list, max_length=50)
    unknowns: list[str] = Field(default_factory=list, max_length=50)


class ToolDefinition(ApiModel):
    """One allowlisted tool the AI may propose (Phase 8, docs/agent-tools.md).

    The catalog is sent by the backend (the application owns the allowlist); the AI
    service only renders it into the prompt and parses proposals. It never executes
    tools — validation, authorization, and execution happen in .NET.
    """

    name: str = Field(min_length=1, max_length=100)
    description: str = Field(default="", max_length=2000)
    input_schema: dict[str, object] = Field(default_factory=dict)


class ToolResultItem(ApiModel):
    """Result of one executed/rejected tool call, fed back to the AI (untrusted data)."""

    tool_call_id: str = Field(min_length=1, max_length=100)
    tool_name: str = Field(min_length=1, max_length=100)
    status: Literal["executed", "rejected", "failed", "not_allowed", "timeout"] = "executed"
    output: str | None = Field(default=None, max_length=100_000)
    error_code: str | None = Field(default=None, max_length=100)


class IncidentAnalysisRequest(ApiModel):
    """Incident investigation request (docs/ai-service-boundary.md §5).

    The backend owns job orchestration and passes the normalized context; the AI
    service performs hybrid retrieval and returns a grounded investigation.
    Phase 8: when `tool_catalog` is non-empty the service returns one turn at a
    time (`kind: tool_call | final`) and `tool_results` carries executed tool
    outputs back into the next turn. Tools are proposals only — never executed here.
    """

    project_id: str = Field(min_length=1, max_length=100)
    schema_version: str = "1"
    prompt_version: str | None = Field(default=None, max_length=100)
    analysis_id: str | None = Field(default=None, max_length=100)
    incident: IncidentContextItem
    retrieved_documents: list[RetrievedDocumentItem] = Field(default_factory=list, max_length=50)
    max_evidence_chunks: int | None = Field(default=None, ge=1, le=100)
    max_chars_per_chunk: int | None = Field(default=None, ge=500, le=100_000)
    # Phase 8 tool loop (docs/agent-tools.md): the allowlist + accumulated results.
    tool_catalog: list[ToolDefinition] = Field(default_factory=list, max_length=32)
    tool_results: list[ToolResultItem] = Field(default_factory=list, max_length=16)


class RiskAnalysisRequest(ApiModel):
    """The evidence package the ASP.NET backend assembled (docs/ai-service-boundary.md §3)."""

    project_id: str = Field(min_length=1, max_length=100)
    schema_version: str = "1"
    prompt_version: str | None = Field(default=None, max_length=100)
    change_summary: str = Field(min_length=1, max_length=5000)
    changed_files: list[ChangedFile] = Field(min_length=1, max_length=200)
    impacted_components: list[ImpactedComponentItem] = Field(default_factory=list, max_length=500)
    api_contracts: list[ApiContractItem] = Field(default_factory=list, max_length=200)
    retrieved_documents: list[RetrievedDocumentItem] = Field(default_factory=list, max_length=50)
    historical_incidents: list[HistoricalIncidentItem] = Field(default_factory=list, max_length=50)
    runbooks: list[RunbookItem] = Field(default_factory=list, max_length=50)
    # --- Phase 4 change-intelligence context (from the Roslyn analyzer via the backend) ---
    changed_symbols: list[ChangedSymbolItem] = Field(default_factory=list, max_length=500)
    impacted_symbols: list[ChangedSymbolItem] = Field(default_factory=list, max_length=1000)
    dependency_edges: list[DependencyEdgeItem] = Field(default_factory=list, max_length=1000)
    dependency_paths: list[str] = Field(default_factory=list, max_length=500)
    impacted_services: list[str] = Field(default_factory=list, max_length=100)
    # Optional per-request budget overrides (clamped by server settings; §24 of the brief).
    max_evidence_chunks: int | None = Field(default=None, ge=1, le=100)
    max_chars_per_chunk: int | None = Field(default=None, ge=500, le=100_000)
