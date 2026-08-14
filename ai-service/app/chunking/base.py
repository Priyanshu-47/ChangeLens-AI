"""Chunking primitives: Chunk model, content normalization, content hashing.

Content hashing drives idempotency: same normalized content ⇒ same hash ⇒ the
ingestion pipeline skips re-chunking/re-embedding (docs/rag-architecture.md §4,
docs/ai-service-boundary.md §3).
"""

from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from typing import Protocol


@dataclass
class Chunk:
    """One structure-aware chunk ready for persistence."""

    chunk_type: str  # Class | Method | Constructor | Property | Section | File | …
    content: str
    symbol: str | None = None
    path: str | None = None
    metadata: dict = field(default_factory=dict)  # e.g. {"namespace": "...", "class": "..."}


def normalize_content(content: str) -> str:
    """Deterministic normalization: CRLF→LF, trailing whitespace trimmed."""
    return "\n".join(line.rstrip() for line in content.replace("\r\n", "\n").split("\n")).strip()


def content_hash(content: str) -> str:
    """sha256 of the normalized content — the idempotency key for a document/chunk."""
    return hashlib.sha256(normalize_content(content).encode("utf-8")).hexdigest()


class Chunker(Protocol):
    def chunk(self, content: str, *, path: str | None = None) -> list[Chunk]: ...
