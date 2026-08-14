"""SQLAlchemy models — `ai` schema only (docs/domain-model.md §6, ADR-0003).

The AI service owns: documents, document_chunks, embeddings. The `app` schema
(projects, incidents, …) is owned by the ASP.NET backend; the AI service stores
backend-provided identifiers (document id, project id, incident id, service) as
plain text metadata, never as foreign keys into the app schema.
"""

from __future__ import annotations

import uuid
from datetime import datetime, timezone

from pgvector.sqlalchemy import Vector
from sqlalchemy import DateTime, ForeignKey, Index, Integer, Text, UniqueConstraint
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship

# The pgvector column dimension is fixed at migration time. It must equal the
# configured EMBEDDING_DIMENSION (which derives from the embedding model). Changing
# it requires a new migration AND a full re-index (docs/llm-integration.md §2).
AI_EMBEDDING_DIMENSION = 768


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class Base(DeclarativeBase):
    pass


class AiDocument(Base):
    """A source document (source code file, incident, runbook, …) in the ai schema.

    `id` is the BACKEND document id (a foreign key into the app schema, never
    generated here — docs/ai-service-boundary.md §3). No DB-level FK crosses into the
    app schema: schema ownership is strict (ADR-0003).
    """

    __tablename__ = "documents"
    __table_args__ = (
        Index("ix_ai_documents_project_type", "project_id", "document_type"),
        Index("ix_ai_documents_path", "project_id", "path"),
        {"schema": "ai"},
    )

    id: Mapped[str] = mapped_column(Text, primary_key=True)
    project_id: Mapped[str] = mapped_column(Text, nullable=False)
    document_type: Mapped[str] = mapped_column(Text, nullable=False)
    source: Mapped[str | None] = mapped_column(Text, nullable=True)
    path: Mapped[str | None] = mapped_column(Text, nullable=True)
    title: Mapped[str | None] = mapped_column(Text, nullable=True)
    language: Mapped[str | None] = mapped_column(Text, nullable=True)
    service: Mapped[str | None] = mapped_column(Text, nullable=True)
    incident_id: Mapped[str | None] = mapped_column(Text, nullable=True)
    environment: Mapped[str | None] = mapped_column(Text, nullable=True)
    content_hash: Mapped[str] = mapped_column(Text, nullable=False)
    # DB column is named "metadata"; the attribute avoids SQLAlchemy's reserved name.
    metadata_json: Mapped[dict] = mapped_column("metadata", JSONB, default=dict, nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utcnow, nullable=False
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utcnow, onupdate=utcnow, nullable=False
    )

    chunks: Mapped[list["AiDocumentChunk"]] = relationship(
        back_populates="document", cascade="all, delete-orphan", passive_deletes=True
    )


class AiDocumentChunk(Base):
    """A structure-aware chunk of a document, with retrieval-relevant metadata."""

    __tablename__ = "document_chunks"
    __table_args__ = (
        Index("ix_ai_chunks_project", "project_id"),
        Index("ix_ai_chunks_document", "document_id"),
        Index("ix_ai_chunks_path", "project_id", "path"),
        {"schema": "ai"},
    )

    id: Mapped[uuid.UUID] = mapped_column(
        UUID(as_uuid=True), primary_key=True, default=uuid.uuid4
    )
    document_id: Mapped[str] = mapped_column(
        Text, ForeignKey("ai.documents.id", ondelete="CASCADE"), nullable=False
    )
    # Denormalized for hard project isolation at query time (never trusted from input).
    project_id: Mapped[str] = mapped_column(Text, nullable=False)
    chunk_type: Mapped[str | None] = mapped_column(Text, nullable=True)  # Class|Method|Section|…
    symbol: Mapped[str | None] = mapped_column(Text, nullable=True)
    path: Mapped[str | None] = mapped_column(Text, nullable=True)
    language: Mapped[str | None] = mapped_column(Text, nullable=True)
    service: Mapped[str | None] = mapped_column(Text, nullable=True)
    incident_id: Mapped[str | None] = mapped_column(Text, nullable=True)
    environment: Mapped[str | None] = mapped_column(Text, nullable=True)
    content_hash: Mapped[str] = mapped_column(Text, nullable=False)
    content: Mapped[str] = mapped_column(Text, nullable=False)
    metadata_json: Mapped[dict] = mapped_column("metadata", JSONB, default=dict, nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utcnow, nullable=False
    )

    document: Mapped[AiDocument] = relationship("AiDocument", back_populates="chunks")
    embedding: Mapped["AiEmbedding | None"] = relationship(
        "AiEmbedding", back_populates="chunk", uselist=False, cascade="all, delete-orphan"
    )


class AiEmbedding(Base):
    """One embedding per chunk, tagged with the model + version that produced it.

    Staleness rule: an embedding is stale when `model_version` differs from the
    configured embedding model version — the ingestion service re-embeds those chunks
    (docs/llm-integration.md §2: model change ⇒ re-index).
    """

    __tablename__ = "embeddings"
    __table_args__ = (
        UniqueConstraint("chunk_id", name="uq_ai_embeddings_chunk"),
        Index("ix_ai_embeddings_model", "model", "model_version"),
        {"schema": "ai"},
    )

    id: Mapped[uuid.UUID] = mapped_column(
        UUID(as_uuid=True), primary_key=True, default=uuid.uuid4
    )
    chunk_id: Mapped[uuid.UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("ai.document_chunks.id", ondelete="CASCADE"),
        nullable=False,
    )
    model: Mapped[str] = mapped_column(Text, nullable=False)
    model_version: Mapped[str] = mapped_column(Text, nullable=False)
    dimension: Mapped[int] = mapped_column(Integer, nullable=False)
    vector: Mapped[list[float]] = mapped_column(Vector(AI_EMBEDDING_DIMENSION), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=utcnow, nullable=False
    )

    chunk: Mapped[AiDocumentChunk] = relationship("AiDocumentChunk", back_populates="embedding")
