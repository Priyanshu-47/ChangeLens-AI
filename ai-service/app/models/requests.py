"""Request models for the internal analysis endpoints.

Phase 2: the AI service receives the evidence package assembled by the backend directly
in the request (no retrieval yet — that is Phase 3). All content below is UNTRUSTED data;
the prompt builder marks it as such and the model is instructed never to act on it.
"""

from __future__ import annotations

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


class RetrievalSearchRequest(ApiModel):
    """Hybrid retrieval request (docs/ai-service-boundary.md §3)."""

    project_id: str = Field(min_length=1, max_length=100)
    query: str = Field(min_length=1, max_length=2000)
    document_types: list[str] | None = Field(default=None, max_length=10)
    filters: SearchFilters = Field(default_factory=SearchFilters)
    strategy: Literal["hybrid", "vector", "keyword"] = "hybrid"
    k: int = Field(default=10, ge=1, le=100)
    embedding_model: str | None = Field(default=None, max_length=200)


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
