# AI Service Boundary

> Phase 0 deliverable. Defines exactly what the Python FastAPI service owns, what it never owns, and its internal REST contract with the ASP.NET Core backend.

## 1. Ownership

**The AI service owns the five AI capabilities:**

| Capability | Notes |
| --- | --- |
| Document ingestion | Accepts raw content + metadata from the backend; validates, dedupes via content hash |
| Semantic chunking | Structure-aware chunkers per document type (see [rag-architecture.md](rag-architecture.md)) |
| Embeddings | Provider abstraction (Gemini / local), batched, cached, model-versioned |
| Hybrid retrieval | Vector + keyword + metadata pre-filter + RRF merge (+ optional reranker) |
| Structured LLM reasoning | Workflow A risk analysis, Workflow B investigation, later: tool-call proposals, evaluation |

**It never does:** authentication/authorization, workflow orchestration, business-entity persistence, dependency-graph computation, or deciding *what* to analyze. It is stateless with respect to workflow — every call carries its full context or references stored documents by id.

## 2. Runtime and configuration

- Python 3.12+, FastAPI + Uvicorn, Pydantic v2, SQLAlchemy 2 + Alembic (ai schema only), `google-genai` SDK, optional `sentence-transformers` (local embeddings) and cross-encoder (local reranker).
- Config via `pydantic-settings` from environment variables (`.env.example`), validated at startup; unknown/empty required vars fail fast with a clear message.
- The service never holds user JWT tokens and never issues them.

## 3. Internal REST contract (`/internal/v1`, shared-secret auth)

All requests carry `X-Internal-Key: <INTERNAL_API_KEY>` and `X-Contract-Version: 1`. Responses use the same error envelope as the public API. Every endpoint records usage metadata (latency, tokens, cost estimate) for the caller to persist in `analysis_runs`.

### `POST /internal/v1/ingest/documents`
```json
{
  "projectId": "…",
  "documents": [
    {
      "id": "…",                    // backend document id (foreign key, never generated here)
      "documentType": "SourceCode", // SourceCode | OpenApi | Incident | Runbook | DeploymentRecord
      "repositoryId": "…", "serviceId": "…", "incidentId": "…",
      "filePath": "src/clients/AuthClient.cs", "language": "csharp",
      "environment": null, "content": "…raw…", "contentHash": "sha256:…"
    }
  ],
  "reindex": false                   // true forces chunk+embed even if hash unchanged
}
```
→ `202` + `{ "documentIds": [], "chunkCount": 0, "skipped": 0, "errors": [] }`. Idempotent: same `contentHash` + same embedding model → skipped.

### `POST /internal/v1/retrieval/search`
```json
{
  "projectId": "…",                       // REQUIRED — hard tenant filter, server-enforced
  "query": "token refresh signing key mismatch",
  "documentTypes": ["Incident", "Runbook"],  // optional
  "filters": { "serviceId": "…", "language": "csharp", "environment": "production" },
  "strategy": "hybrid",                   // hybrid | vector | keyword (used by evaluation)
  "k": 10,
  "embeddingModel": "text-embedding-004"  // optional override
}
```
→ `200`:
```json
{
  "results": [
    { "documentId": "…", "chunkId": "…", "chunkType": "Class",
      "content": "…", "metadata": { "documentType": "Incident", "incidentId": "INC-182", "filePath": null },
      "score": 0.83, "sources": { "vector": 0.81, "keyword": 0.55 } }
  ],
  "usage": { "queries": ["token refresh signing key mismatch"], "latencyMs": 340, "tokens": { "embedding": 120 }, "strategy": "hybrid" }
}
```
The backend stores `results` (ids, scores) into `analysis_runs.retrieved_documents` verbatim — this is the retrieval audit trail and the evaluation input.

### `POST /internal/v1/analysis/risk`
Request: the **evidence package** the backend assembled — changed files with parsed symbol references, dependency impact set, API contracts, retrieved documents (content + metadata + scores), historical incidents, runbooks. Plus `projectId`, `schemaVersion`, `promptVersion` (backend may pin).
Response: a validated `RiskAnalysisResult` (the JSON shape from [api-contract.md](api-contract.md) §3) wrapped with `usage` metadata. **Schema validation happens inside the AI service before any response leaves it** ([ADR-0007](adr/0007-structured-output-schema-validation.md)).

### `POST /internal/v1/analysis/incident`
Same shape philosophy: incident + normalized events + deployments-in-window + retrieved context in; validated `IncidentInvestigationResult` out.

### `POST /internal/v1/evaluations/run` (Phase 7)
`{ "datasetId": "…", "strategies": ["keyword", "vector", "hybrid", "pipeline"], "limit": 20 }` → 202; results land in the `ai` schema and are also returned for the backend to persist in `evaluation_runs`.

### Health
`GET /internal/v1/health/live` (process up), `GET /internal/v1/health/ready` (DB reachable, configured embedding/LLM models resolvable — **this is where a misconfigured/deprecated model name is caught at startup**, see [llm-integration.md](llm-integration.md)).

## 4. Reliability contract

- **Timeouts:** the backend enforces a generous HTTP timeout (e.g. 120s per reasoning call); the AI service enforces its own Gemini call timeout (e.g. 60s) with **retry-on-429/5xx with exponential backoff + jitter, max 3** — never blind retries on validation failures.
- **Payload limits:** document content capped (e.g. 5 MB per document, 100 docs/batch); retrieval request bodies capped.
- **Safe failure:** on unrecoverable validation failure the service returns `422` with `{ code: "AI_VALIDATION_FAILED", details: { attempts, errors } }` — it never returns unvalidated prose as a "result".
- **Idempotency keys** accepted on ingest; analysis calls are naturally idempotent (pure function of their input package).

## 5. Tool-use boundary (Phase 6)

The backend owns tool **schemas, execution, authorization, and audit**; the AI service only proposes calls:

```
AI service (LLM turn):  "propose tool call: search_incidents(query='token refresh', project='…')"
Backend:                validates input against schema → authorizes project → executes (SQL/API)
                        → appends result to the conversation → returns to AI service for the next turn
```

Every proposed/executed call is appended to `analysis_runs.tool_calls` with outcome, latency, and audit-logged. No tool executes without backend authorization ([ADR-0008](adr/0008-controlled-tool-use.md), [security-model.md](security-model.md)).

## 6. Non-responsibilities checklist

- ❌ AuthN/AuthZ (including project scoping — it *enforces* the project filter the backend passes, but never derives it)
- ❌ Business entity persistence (`app` schema)
- ❌ Orchestrating multi-step workflows
- ❌ Direct calls from the frontend (backend is the only client)
- ❌ Storing API keys for other providers (Gemini key comes from env/config of this service)
