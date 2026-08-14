# ADR-0003: One PostgreSQL instance, two schemas, pgvector

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

The system needs relational persistence (.NET domain) and vector persistence (retrieval). Options: two databases/instances; a managed vector database (Pinecone/Qdrant/Weaviate); or one PostgreSQL instance with pgvector. The project is $0-first and portfolio-scale.

## Decision

One PostgreSQL instance (Docker `pgvector/pgvector` image) with two logical schemas:
- `app` — EF Core migrations, owned by the backend, all business entities.
- `ai` — Alembic migrations, owned by the AI service, documents/chunks/embeddings.

Ownership is strict: the backend never writes to `ai`; the AI service never writes to `app` and never reads it via SQL (retrieval results flow through the internal API). The AI service *does* read/write `ai` directly, which is its own schema.

## Consequences

- $0, one container, one backup, one healthcheck; relational and vector data co-located (transactional consistency where needed, e.g. document + chunks).
- pgvector HNSW supports the hybrid retrieval design (ADR-0004).
- Cost: schemas share a server (no independent scaling — irrelevant at portfolio scale); migration discipline is mandatory; a future scale-out is documented in Phase 10 notes (managed Postgres with pgvector, or a dedicated vector store behind the same abstraction).
- No Redis/Kafka/etc. — no demonstrated requirement (also ADR-0009 keeps async within the app via job rows).
