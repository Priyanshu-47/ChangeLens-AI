"""Chunker selection by document type (docs/rag-architecture.md §3).

Document types (Phase 3): SourceCode, Incident, Runbook. ApiDefinition and
DeploymentRecord land with their data sources in later phases — the registry
falls back to heading-based sections so unknown types still ingest cleanly.
"""

from __future__ import annotations

from .base import Chunk, Chunker, content_hash, normalize_content
from .code import CodeChunkerFactory
from .text import IncidentChunker, RunbookChunker, SectionChunker

__all__ = [
    "Chunk",
    "Chunker",
    "content_hash",
    "normalize_content",
]


def get_chunker(document_type: str, language: str | None = None) -> Chunker:
    if document_type == "SourceCode":
        return CodeChunkerFactory.for_language(language)
    if document_type == "Incident":
        return IncidentChunker()
    if document_type == "Runbook":
        return RunbookChunker()
    # ApiDefinition / DeploymentRecord (later phases): heading-aware sections for now.
    return SectionChunker()
