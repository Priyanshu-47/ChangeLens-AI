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
  "embeddingModel": "gemini-embedding-2"   // optional override (GA model)
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

> **Phase 2 status:** implemented end-to-end with the evidence package passed directly in the request (changed files + change summary; the retrieval-backed sections are empty until Phase 3). The concrete request/response models live in `ai-service/app/models/` (`requests.py`, `responses.py`); `AnalysisUsage` carries model, prompt version, latency, tokens (null when the provider exposes none), estimated cost (null unless pricing is configured), validation status, repair attempts, and an evidence-truncation flag. `promptVersion` pins a known versioned prompt (`risk-v1`) or falls back to the default.

### `POST /internal/v1/analysis/incident` (Phase 5)
The backend owns job orchestration; this endpoint is **one synchronous analysis call** that
retrieves evidence from the incident context and returns a grounded investigation:
```json
{
  "projectId": "…",                    // REQUIRED — hard tenant filter, server-enforced
  "analysisId": "…",                   // backend analysis-run id (for logs/trace)
  "promptVersion": "incident-v1",      // optional; pins a known versioned prompt
  "incident": {
    "title": "HTTP 401 after JWT signing-key rotation",
    "severity": "Sev1", "status": "Open", "environment": "production", "service": "acmepay-api",
    "summary": "…", "startedAtUtc": "…", "detectedAtUtc": "…",
    "symptoms": [ "JwtSecurityTokenHandler: IDX10503 signature validation failed" ],
    "knownFacts": [ "Severity: Sev1" ],
    "unknowns": [ "No deployment timestamp was supplied." ],
    "timeline": [ { "occurredAtUtc": "…", "type": "deployment", "source": "cicd", "message": "…", "rawData": null } ]
  },
  "maxEvidenceChunks": null, "maxCharsPerChunk": null   // optional budget overrides (clamped)
}
```
→ `200` with `analysisType: "incident"`, `usage`, and a validated `IncidentAnalysisResult`:
```json
{
  "rootCauseCandidates": [
    { "candidateId": "…", "title": "…", "confidence": 0.74, "status": "Candidate",
      "evidenceIds": [ "chunk:…" ], "reasoning": "…", "unknowns": [] }
  ],
  "remediation": { "immediateMitigation": "…", "investigationSteps": [], "recommendedRemediation": null,
                    "validationSteps": [], "rollbackConsideration": "…", "insufficientEvidence": false },
  "unknowns": [ "No database telemetry was available." ],
  "evidence": [ { "id": "chunk:…", "type": "Document", "source": "chunk:…", "summary": "…", "metadata": {} } ]
}
```
Grounding (deterministic, post-validation, [ADR-0007](adr/0007-structured-output-schema-validation.md)):
every `rootCauseCandidates[].evidenceIds` must contain **at least one** id from the evidence
index and every id must exist — an empty list is rejected by schema validation and unknown
ids by the grounding check. Retrieval queries are generated server-side from the incident
context (title, symptom/error messages, service, symbol-like terms) preserving exact
identifiers for the keyword leg (brief §13–14).

### `POST /internal/v1/evaluations/run` (Phase 8)
`{ "datasetId": "…", "strategies": ["keyword", "vector", "hybrid", "pipeline"], "limit": 20 }` → 202; results land in the `ai` schema and are also returned for the backend to persist in `evaluation_runs`.

### Health
`GET /internal/v1/health/live` (process up), `GET /internal/v1/health/ready` (DB reachable, configured embedding/LLM models resolvable — **this is where a misconfigured/deprecated model name is caught at startup**, see [llm-integration.md](llm-integration.md)).

## 4. Reliability contract

- **Timeouts:** the backend enforces a generous HTTP timeout (e.g. 120s per reasoning call); the AI service enforces its own Gemini call timeout (e.g. 60s) with **retry-on-429/5xx with exponential backoff + jitter, max 3** — never blind retries on validation failures. The backend additionally wraps each async job in a per-job timeout (default 600s) so no analysis stays `Running` forever.
- **Retries:** transient failures (429/504/502) are retried by the backend worker with bounded exponential backoff (default 2 retries); 400/401/403/422 validation failures are never retried.
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
