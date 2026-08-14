"""Heading-aware chunkers for structured text documents.

Incident records and runbooks stay semantically coherent: chunks are split on
headings (## Section) and each chunk keeps its heading context (docs/rag-architecture.md
§3). A small document with no headings stays ONE chunk — never meaningless fragments.
"""

from __future__ import annotations

from .base import Chunk


class SectionChunker:
    """Splits markdown-like text on level-1/2 headings, keeping heading context."""

    def chunk(self, content: str, *, path: str | None = None) -> list[Chunk]:
        lines = content.replace("\r\n", "\n").split("\n")
        sections: list[tuple[str | None, list[str]]] = []  # (heading, lines)
        current_heading: str | None = None
        current: list[str] = []

        def flush() -> None:
            if current:
                sections.append((current_heading, current))

        for line in lines:
            stripped = line.strip()
            if stripped.startswith("## ") or stripped.startswith("# "):
                flush()
                current_heading = stripped.lstrip("#").strip()
                current = [line]
            else:
                current.append(line)
        flush()

        if len(sections) <= 1:
            return [Chunk(chunk_type="Section", content=content, path=path, symbol=current_heading)]

        chunks: list[Chunk] = []
        for heading, section_lines in sections:
            text = "\n".join(section_lines).strip()
            if not text:
                continue
            chunks.append(
                Chunk(
                    chunk_type="Section",
                    symbol=heading,
                    content=text,
                    path=path,
                    metadata={"heading": heading},
                )
            )
        return chunks


class IncidentChunker(SectionChunker):
    """Incident documents: sections (Symptom / Timeline / Root Cause / Resolution /
    Lessons Learned) stay individually retrievable while sharing document metadata."""


class RunbookChunker(SectionChunker):
    """Runbooks: sections (Symptoms / Diagnosis / Common Causes / Resolution / Rollback)
    stay individually retrievable; the title stays the runbook's document title."""
