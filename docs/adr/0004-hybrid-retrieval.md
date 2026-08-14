# ADR-0004: Hybrid retrieval, not vector-only

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

RAG systems often default to vector similarity alone. For code change risk and incident investigation, exact identifiers (`AuthClient`, `INC-182`, `RefreshAsync`) and metadata (project, service, environment, document type) matter as much as semantics, and the brief explicitly requires hybrid retrieval with dependency relationships.

## Decision

Retrieval is a four-part pipeline:
1. **Metadata pre-filtering** — `project_id` always; optional `document_type`, `language`, `service_id`, `environment`, `incident_id`, time range — applied before scoring.
2. **Vector leg** — embeddings via the provider abstraction, pgvector cosine, HNSW index, top-k.
3. **Keyword leg** — Postgres full-text search with an identifier-aware tokenizer (camelCase/snake_case splitting), top-k.
4. **Merge** — Reciprocal Rank Fusion (`k=60`), returning per-source scores for transparency.

Dependency relationships join retrieval as a fourth dimension: the backend enriches retrieved code chunks with dependents/callees from the graph. Reranking is pluggable but off by default in MVP (no reranker; local cross-encoder or Vertex reranker behind the same interface if needed).

## Consequences

- Robust matches on identifiers, code, and prose; per-leg scores make results explainable.
- Evaluation can compare legs independently (keyword-only vs vector-only vs hybrid) — required by the evaluation framework (ADR-0010).
- Cost: more moving parts, a custom tokenizer, and RRF tuning; all unit-testable offline with local embeddings.
