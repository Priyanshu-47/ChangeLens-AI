"""Ingestion pipeline (docs/rag-architecture.md §4, ai-service-boundary.md §3).

    document -> normalize -> hash -> structure-aware chunking -> persist chunks
             -> batch embeddings (retry transient, report failures) -> persist vectors

Deterministic and idempotent:
- unchanged content (same hash) => SKIPPED (no re-chunk, no re-embed)
- unchanged content, stale embedding model => re-embed only
- changed content => new chunks + new embeddings (old chunks cascade-deleted)
- batch embedding failures never silently drop chunks: they are reported in `errors`
  and remain visible to keyword search; the next ingest retries them.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field

from sqlalchemy import select

from ..chunking import content_hash, get_chunker
from ..config import Settings
from ..db import Database
from ..embeddings.base import EmbeddingDimensionError, EmbeddingError, IEmbeddingProvider
from ..models.ai import AiDocument, AiDocumentChunk, AiEmbedding
from ..models.requests import IngestDocumentsRequest

logger = logging.getLogger(__name__)


@dataclass
class IngestResult:
    document_ids: list[str] = field(default_factory=list)
    chunk_count: int = 0
    skipped: int = 0
    errors: list[dict] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "documentIds": self.document_ids,
            "chunkCount": self.chunk_count,
            "skipped": self.skipped,
            "errors": self.errors,
        }


class IngestionService:
    def __init__(self, *, db: Database, embedding: IEmbeddingProvider, settings: Settings):
        self._db = db
        self._embedding = embedding
        self._settings = settings

    def ingest(self, request: IngestDocumentsRequest) -> IngestResult:
        result = IngestResult()
        pending_embeds: list[AiDocumentChunk] = []

        with self._db.session() as session:
            for item in request.documents:
                try:
                    status = self._ingest_one(session, request.project_id, item, request.reindex, pending_embeds)
                except Exception as exc:  # noqa: BLE001 - one bad document must not kill the batch
                    logger.warning("Ingestion failed for document %s: %s", item.id, exc)
                    result.errors.append({"documentId": item.id, "error": str(exc)})
                    continue

                result.document_ids.append(item.id)
                if status == "skipped":
                    result.skipped += 1
                else:
                    result.chunk_count += status

            # Embed all new/stale chunks in batches; failures are reported, never silent.
            batch_errors = self._embed_batches(session, pending_embeds)
            result.errors.extend(batch_errors)

        logger.info(
            "ingestion_complete",
            extra={"documents": len(result.document_ids), "chunks": result.chunk_count,
                   "skipped": result.skipped, "errors": len(result.errors)},
        )
        return result

    def _ingest_one(self, session, project_id: str, item, reindex: bool, pending_embeds: list) -> int:
        hash_value = content_hash(item.content)
        existing = session.get(AiDocument, item.id)

        if existing is not None and existing.project_id != project_id:
            raise ValueError(f"Document {item.id!r} already belongs to another project.")

        if existing is not None and existing.content_hash == hash_value and not reindex:
            self._update_metadata(session, existing, project_id, item, hash_value)
            stale = self._stale_chunks(session, existing.id)
            if not stale:
                return "skipped"
            pending_embeds.extend(stale)
            logger.info("re-embedding %d stale chunks for %s", len(stale), item.id)
            return "skipped"

        # New or changed content: re-chunk from scratch (old chunks cascade-delete).
        chunker = get_chunker(item.document_type, item.language)
        chunks = chunker.chunk(item.content, path=item.file_path)

        if existing is not None:
            session.delete(existing)
            session.flush()  # cascade removes old chunks + embeddings

        doc = AiDocument(
            id=item.id,
            project_id=project_id,
            document_type=item.document_type,
            source="backend",
            path=item.file_path,
            title=item.title,
            language=item.language,
            service=item.service_id,
            incident_id=item.incident_id,
            environment=item.environment,
            content_hash=hash_value,
            metadata_json={"repositoryId": item.repository_id},
        )
        session.add(doc)
        session.flush()

        for chunk in chunks:
            row = AiDocumentChunk(
                document_id=doc.id,
                project_id=project_id,
                chunk_type=chunk.chunk_type,
                symbol=chunk.symbol,
                path=chunk.path,
                language=item.language,
                service=item.service_id,
                incident_id=item.incident_id,
                environment=item.environment,
                content_hash=content_hash(chunk.content),
                content=chunk.content,
                metadata_json=chunk.metadata,
            )
            session.add(row)
            pending_embeds.append(row)

        # Flush now so chunk UUIDs (Python-side defaults) are assigned before the
        # embedding loop reads them — otherwise chunk_id would be NULL at insert.
        session.flush()
        return len(chunks)

    @staticmethod
    def _update_metadata(session, doc: AiDocument, project_id: str, item, hash_value: str) -> None:
        doc.project_id = project_id
        doc.document_type = item.document_type
        doc.path = item.file_path
        doc.title = item.title
        doc.language = item.language
        doc.service = item.service_id
        doc.incident_id = item.incident_id
        doc.environment = item.environment
        doc.content_hash = hash_value
        doc.metadata_json = {"repositoryId": item.repository_id}
        for chunk in doc.chunks:
            chunk.service = item.service_id
            chunk.incident_id = item.incident_id
            chunk.environment = item.environment

    def _stale_chunks(self, session, document_id: str) -> list[AiDocumentChunk]:
        """Chunks whose embedding is missing or from an older model version."""
        expected = self._embedding.model_version
        rows = (
            session.execute(
                select(AiDocumentChunk)
                .outerjoin(AiEmbedding, AiEmbedding.chunk_id == AiDocumentChunk.id)
                .where(AiDocumentChunk.document_id == document_id)
                .where(
                    (AiEmbedding.id.is_(None)) | (AiEmbedding.model_version != expected)
                )
            )
            .scalars()
            .all()
        )
        return list(rows)

    def _embed_batches(self, session, chunks: list[AiDocumentChunk]) -> list[dict]:
        if not chunks:
            return []
        errors: list[dict] = []
        batch_size = self._settings.embedding_batch_size
        for i in range(0, len(chunks), batch_size):
            batch = chunks[i : i + batch_size]
            try:
                result = self._embedding.embed_texts([c.content for c in batch])
            except EmbeddingDimensionError as exc:
                logger.error("Embedding dimension mismatch for batch: %s", exc)
                errors.extend({"chunkId": str(c.id), "error": str(exc)} for c in batch)
                continue
            except EmbeddingError as exc:
                # Failed batch is reported and retried on the next ingest — never silent.
                logger.warning("Embedding batch failed (%d chunks): %s", len(batch), exc)
                errors.extend({"chunkId": str(c.id), "error": f"embedding_failed: {exc}"} for c in batch)
                continue

            if len(result.vectors) != len(batch):
                errors.extend({"chunkId": str(c.id), "error": "embedding count mismatch"} for c in batch)
                continue

            for chunk, vector in zip(batch, result.vectors):
                if len(vector) != self._settings.embedding_dimension:
                    errors.append(
                        {"chunkId": str(chunk.id), "error": "embedding dimension mismatch"}
                    )
                    continue
                session.add(
                    AiEmbedding(
                        chunk_id=chunk.id,
                        model=result.model,
                        model_version=result.model_version,
                        dimension=result.dimension,
                        vector=vector,
                    )
                )

        return errors
