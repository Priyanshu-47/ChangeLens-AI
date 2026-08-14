# RAG Architecture

> Phase 0 deliverable. How documents become retrievable, and how retrieval happens. Design goal: **hybrid retrieval with metadata filtering and dependency relationships — never vector-only** ([ADR-0004](adr/0004-hybrid-retrieval.md)).

## 1. Pipeline overview

```mermaid
flowchart LR
    SRC["Raw documents<br/>(code, incidents, runbooks, OpenAPI, deployments)"] --> ING["Ingest<br/>validate · dedupe (hash)"]
    ING --> CH["Semantic chunking<br/>(structure-aware)"]
    CH --> EMB["Embed (provider abstraction)"]
    EMB --> ST[("ai schema:<br/>documents · chunks · embeddings")]
    Q["Query / evidence package"] --> RET["Hybrid retrieval<br/>vector + keyword + filters + RRF"]
    RET --> RR["Rerank (optional)"]
    ST --> RET
    RET --> OUT["Ranked results + scores<br/>(recorded in analysis_runs)"]
```

## 2. Semantic chunking (never fixed-N splits)

Chunkers are selected by `documentType` and preserve the semantic hierarchy defined in the brief:

| Document type | Chunk hierarchy | Chunker implementation |
| --- | --- | --- |
| Source code (C#) | File → Class → Method (with signature + body; references to other symbols kept as text) | tree-sitter (C# grammar) — **retrieval granularity only**; deep symbol/dependency analysis is Roslyn's job in .NET ([ADR-0011](adr/0011-static-analysis-vs-chunking.md)) |
| Source code (JS/TS, Python) | File → Class/Function → Method | tree-sitter (best-effort) |
| JSON / YAML | Top-level object per chunk with path metadata | structural walk |
| OpenAPI | Endpoint → (method, request, response, tags) | path-item walk |
| Incident | Incident → (symptom, timeline, root cause, resolution, lessons learned) | structured fields, not raw text |
| Runbook (Markdown) | Heading hierarchy → sections; table rows preserved | Markdown AST |

Each chunk stores `chunk_type`, `heading_path` (e.g. `AuthClient/RefreshAsync`), `char_start/end`, and parent metadata (`file_path`, `language`, `service_id`, `incident_id`, `environment`, `document_type`, `project_id`). Chunk size target ~300–600 tokens with overlap only where the structure requires it (e.g. method body split).

## 3. Embeddings

- **Abstraction:** `EmbeddingProvider` interface with two implementations — `GeminiEmbeddingProvider` (API, free tier, 768-dim `text-embedding-004` default, configurable) and `LocalEmbeddingProvider` (`sentence-transformers`, offline/dev/tests, $0). See [ADR-0006](adr/0006-embedding-provider-abstraction.md).
- **Batch + cache:** embeddings requested in batches; identical content (same hash) never re-embedded.
- **Model-versioned storage:** `embeddings(chunk_id, model, version, vector)`. Changing the embedding model triggers re-indexing of affected documents; old vectors remain for evaluation comparisons ([ADR-0006]).
- **Dimensions are per-model:** vector column dimension matches the configured model; switching models is a migration + re-index event, surfaced in the UI ("re-index required").

## 4. Hybrid retrieval

Single search endpoint executes, per strategy:

1. **Pre-filter (mandatory + optional):** `project_id` is always applied server-side; optional filters from the metadata surface (`document_type`, `language`, `service_id`, `environment`, `incident_id`, `time range`). Filtering happens **before** scoring, not as post-hoc cuts.
2. **Vector leg:** embed the query → pgvector cosine similarity (HNSW index) with filters → top-k (k=50).
3. **Keyword leg:** Postgres full-text search (`tsvector` GIN, English + identifier-aware tokenization so `AuthClient` and `RefreshAsync` match) with the same filters → top-k (k=50).
4. **Merge:** Reciprocal Rank Fusion (RRF, `k=60`) — rank-based, robust to score-scale differences between legs. Scores and per-source components are returned so the UI can show *why* a result surfaced.
5. **Optional rerank:** pluggable `Reranker` — MVP default is **no reranker** (RRF is sufficient at portfolio scale); local cross-encoder available via config; a Gemini/Vertex reranker can be added behind the same interface later (API-key reranking availability varies by account — never a hard dependency).

**Dependency relationships join retrieval:** the backend enriches retrieved *code* chunks with their dependents/callees from the `app` schema dependency graph before building the evidence package — so retrieval is "similar code", and the evidence package is "similar code + what it touches". This is the fourth retrieval dimension from the brief.

## 5. Retrieval observability (feeds evaluation)

Every search records: normalized queries, applied filters, per-leg top results with scores, final merged ranking, latency, embedding tokens. The backend persists this in `analysis_runs.retrieval_queries` / `retrieved_documents`. The evaluation framework replays the same queries against ground truth to compute Recall@K, precision, MRR for each strategy ([ADR-0010](adr/0010-evaluation-first-class.md)).

## 6. Known limits & mitigations

| Limit | Mitigation |
| --- | --- |
| pgvector at very large scale (100M+ chunks) | Not an MVP concern; HNSW + filters scale fine to portfolio scale; document migration path to a managed vector store in Phase 10 notes |
| English-centric full-text tokenization for code | Custom identifier tokenizer (camelCase/snake_case splitting) is part of the keyword leg |
| Chunk boundary artifacts (method split mid-body) | Overlap within method bodies only; chunk_type metadata lets the LLM know it may need adjacent chunks — the backend can fetch siblings on request |
| Embedding model drift | Model-versioned embeddings + re-index workflow + evaluation detects quality regression before it ships |
