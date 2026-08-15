# ai-service — Python FastAPI AI Service

> **Phase 3 — complete.** FastAPI + Gemini structured-output service with hybrid RAG: provider abstraction, versioned layered prompts, Pydantic validation with bounded repair and safe failure, grounding enforcement, correlation-id tracing, internal-key auth, mock providers for $0 dev/tests, idempotent ingestion, structure-aware chunking, pgvector embeddings, and RRF hybrid retrieval feeding the analysis pipeline.

The AI capability provider of ChangeLens. It owns AI integration (providers, prompts, structured output, AI-specific validation) and the `ai` schema (ingestion, embeddings, retrieval). It **never** owns users, projects, incidents, authorization, or business state; the ASP.NET backend is the only client ([docs/ai-service-boundary.md](../docs/ai-service-boundary.md), [ADR-0002](../docs/adr/0002-service-boundary.md)).

## Architecture

```text
ASP.NET Core  ── POST /internal/v1/analysis/risk (X-Internal-Key, X-Correlation-ID) ──▶  FastAPI
                                                                                          │
                                                    ┌───────────────────────────────────┘
                                                    ▼
                                            RetrievalService (hybrid)
                                             vector (pgvector)  +  keyword (FTS)  →  RRF
                                                    │                │
                                                    ▼                ▼
                                            ai schema (PostgreSQL): documents · chunks · embeddings
                                                    │
                                                    ▼
                                            IAIProvider (Protocol)
                                              ├── GeminiProvider  (google-genai, structured outputs)
                                              └── MockAIProvider  (deterministic, AI_PROVIDER=mock)
```

The pipeline per analysis: change request → **hybrid retrieval (auto)** → evidence package (`chunk:<uuid>` ids) → layered prompt → provider call → Pydantic validation → deterministic post-checks (confidence bounds, array caps, **grounding rule**) → success, or bounded repair (max 2) → safe failure (`422 AI_VALIDATION_FAILED`) — unvalidated prose is never returned ([ADR-0007](../docs/adr/0007-structured-output-schema-validation.md)).

## Phase 3 subsystems

- **Ingestion** (`POST /internal/v1/ingest/documents`): `document → normalize → sha256 → structure-aware chunking → persist chunks → batch embeddings → persist vectors`. Idempotent: unchanged content skips re-chunk/re-embed; changed content re-chunks (old chunks cascade); stale embedding model re-embeds only. Batch embedding failures are reported per chunk and retried on the next ingest — never silently dropped.
- **Chunking** (`app/chunking/`): tree-sitter for code (csharp/javascript/typescript/python → Class/Interface/Method/Constructor/Property chunks with namespace+class metadata; unknown languages get one honest File chunk), heading-aware section chunkers for incidents and runbooks. Never fixed-N splits.
- **Embeddings** (`app/embeddings/`): `IEmbeddingProvider` protocol, `GeminiEmbeddingProvider` (default `gemini-embedding-2`, 768-dim via `output_dimensionality`, configurable — the retired `text-embedding-004` is never a default) and deterministic `MockEmbeddingProvider` (gram-overlap vectors, $0). Dimension validated per vector; embedding calls happen only during ingestion and query-time search — never at startup or on health.
- **Retrieval** (`app/retrieval/`): vector leg (pgvector cosine, HNSW), keyword leg (PostgreSQL FTS over generated `content_tsv`, `simple` config — exact technical terms like `TimeoutException`/`401`/`JWT`), optional metadata filters (`documentType`, `serviceId`, `language`, `environment`), merged via configurable RRF (`RRF_K=60`). `project_id` is a hard server-side filter inside every SQL statement. Results carry per-source scores (`vector` similarity, `keyword` rank) so the UI can explain *why* a result surfaced.
- **Schema** (`ai` only, ADR-0003): migrated by Alembic (`alembic upgrade head` runs at container start); pgvector extension + HNSW index + GIN tsvector index created by the initial migration.

## Prerequisites

- Python **3.12+** (developed and verified on 3.14)
- A Gemini API key (free tier) for real LLM calls — **not required** for tests or mock mode

## Local setup

```bash
cd ai-service
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements-dev.txt   # Windows (use bin/ on POSIX)
cp .env.example .env           # fill in values (never commit .env)
```

Run with the **mock provider** (no API key, deterministic output):

```bash
AI_PROVIDER=mock INTERNAL_API_KEY=change-me-internal-key .venv/Scripts/python -m uvicorn app.main:app --port 8000
```

Run with **Gemini**:

```bash
AI_PROVIDER=gemini GEMINI_API_KEY=<your key> INTERNAL_API_KEY=change-me-internal-key \
  .venv/Scripts/python -m uvicorn app.main:app --port 8000
```

## Environment variables

All config is environment-driven (`pydantic-settings`, validated at startup — missing required values fail fast with a clear message). Full list in [`.env.example`](.env.example).

| Variable | Required | Default | Notes |
| --- | --- | --- | --- |
| `INTERNAL_API_KEY` | yes | — | Shared secret; the backend sends it as `X-Internal-Key`. Min 8 chars |
| `AI_PROVIDER` | no | `gemini` | `gemini` or `mock` (deterministic stand-in, no key needed) |
| `GEMINI_API_KEY` | when provider=gemini | — | Free tier; never committed |
| `GEMINI_TEXT_MODEL` | no | `gemini-3.7-flash` | Config, not code ([ADR-0005](../docs/adr/0005-llm-provider-abstraction.md)) |
| `GEMINI_TIMEOUT_SECONDS` | no | 60 | Provider call timeout |
| `GEMINI_MAX_RETRIES` | no | 3 | Retries only on 429/5xx, exponential backoff + jitter |
| `GEMINI_MAX_OUTPUT_TOKENS` | no | 8192 | Cost/latency bound |
| `GEMINI_INPUT_PRICE_PER_1M_USD` / `GEMINI_OUTPUT_PRICE_PER_1M_USD` | no | unset | When set, `estimatedCostUsd` is computed (labeled estimate); unset ⇒ `null`, never fabricated |
| `AI_MAX_REPAIR_ATTEMPTS` | no | 2 | Bounded structured-output repair |
| `AI_READINESS_PROBE` | no | `false` | `true` resolves the model name on `/ready` (metadata call). Off ⇒ health/readiness cost zero Gemini |
| `AI_MAX_EVIDENCE_CHARS` | no | 120000 | Token-budget trim for rendered evidence |
| `AI_AUTO_RETRIEVE` | no | `true` | Auto-run hybrid retrieval when the request has no retrieved documents |
| `DATABASE_URL` | no | local dev default | `postgresql+psycopg://...` — the `ai` schema lives here |
| `EMBEDDING_PROVIDER` | no | `gemini` | `gemini` or `mock` (deterministic vectors, $0) |
| `GEMINI_EMBEDDING_MODEL` | no | `gemini-embedding-2` | Embedding model (current GA); must match `EMBEDDING_DIMENSION` |
| `EMBEDDING_DIMENSION` | no | 768 | Vector column dimension; changing the model ⇒ migration + re-index |
| `EMBEDDING_BATCH_SIZE` | no | 32 | Embedding requests are batched (never one request per chunk) |
| `RETRIEVAL_TOP_K` | no | 10 | Final result count |
| `RETRIEVAL_CANDIDATE_K` | no | 50 | Per-leg candidate count before fusion |
| `RRF_K` | no | 60 | Reciprocal Rank Fusion parameter (config, not code) |
| `LOG_LEVEL` | no | `INFO` | |

## Endpoints

| Method | Path | Auth | Purpose |
| --- | --- | --- | --- |
| GET | `/health` | — | Liveness (process up, no Gemini) |
| GET | `/ready` | — | Readiness (config + DB reachability + vector extension; probe only if `AI_READINESS_PROBE=true`) |
| GET | `/internal/v1/health/live` | internal key | Liveness (internal contract) |
| GET | `/internal/v1/health/ready` | internal key | Readiness (internal contract) |
| POST | `/internal/v1/analysis/risk` | internal key | Structured change-risk analysis over an evidence package (RAG-fed) |
| POST | `/internal/v1/ingest/documents` | internal key | Idempotent ingestion (content + metadata, never filesystem access) |
| POST | `/internal/v1/retrieval/search` | internal key | Hybrid retrieval: vector + keyword + filters + RRF |

Swagger/OpenAPI: `http://localhost:8000/docs` (public routes) — the internal routes require the `X-Internal-Key` + `X-Contract-Version: 1` headers.

All internal requests must carry `X-Contract-Version: 1` and (recommended) `X-Correlation-ID`; the service echoes/generates the correlation id and includes it in logs and error envelopes.

## Running the .NET → AI flow (mock)

With both services up and the backend configured with `Ai__BaseUrl=http://localhost:8000` and the same `INTERNAL_API_KEY`:

```bash
curl -s -X POST http://localhost:8000/internal/v1/analysis/risk \
  -H "X-Internal-Key: change-me-internal-key" -H "X-Contract-Version: 1" \
  -H "Content-Type: application/json" \
  -d '{"projectId":"p1","changeSummary":"Changed token refresh logic.","changedFiles":[{"path":"src/AuthClient.cs","changeType":"modified","language":"csharp"}]}'
```

Then from the backend: `POST /api/v1/analyses/change-risk` with a JWT (see [backend/README.md](../backend/README.md)).

## Tests

```bash
cd ai-service
.venv/Scripts/python -m pytest -q          # 88 tests — ZERO Gemini calls, no API key, no database needed
```

Coverage: config validation, model validation (enums/bounds), grounding rule, prompt layering + injection sanitizer, bounded repair + safe failure, retry semantics, error mapping, HTTP contract (auth, correlation, envelopes), structure-aware chunkers, RRF determinism, mock-embedding determinism + similarity, content hashing.

The **PostgreSQL integration suite** (pgvector required — idempotency, content change, vector/keyword/metadata/hybrid retrieval, project isolation) runs only when pointed at a real database:

```bash
TEST_DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens_test" \
  .venv/Scripts/python -m pytest tests/test_db_integration.py -q   # 8 tests
```

Optional **live Gemini smoke tests** (one structured-output call + one embedding call — off by default, protects free-tier quota):

```bash
RUN_GEMINI_TESTS=true GEMINI_API_KEY=<your key> .venv/Scripts/python -m pytest tests/test_gemini_live.py -v -s
```

## Seeding the demo corpus

```bash
# Idempotent: re-running with unchanged files makes ZERO embedding calls and reports SKIPPED.
DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens" \
EMBEDDING_PROVIDER=mock AI_PROVIDER=mock INTERNAL_API_KEY=change-me-internal-key \
  .venv/Scripts/python scripts/seed_demo.py
```

Ingests `data/demo-repository` (24 C# files), `data/demo-incidents` (20), and `data/demo-runbooks` (5) → 49 documents / 247 chunks / 247 vectors under `project_id=demo-project`.

## Migrations

```bash
DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens" .venv/Scripts/python -m alembic upgrade head
```

Migrations touch the `ai` schema only (ADR-0003); the `app` schema belongs to .NET. The vector extension is created idempotently by the bootstrap.

## Provider abstraction

`app/providers/base.py` defines the `IAIProvider` protocol; `GeminiProvider` is the MVP adapter and `MockAIProvider` the deterministic stand-in. The analysis service depends only on the protocol, so an `OpenAIProvider`/`BedrockProvider` can be added without touching orchestration or validation ([ADR-0005](../docs/adr/0005-llm-provider-abstraction.md)). Embeddings use the separate `IEmbeddingProvider` protocol (`app/embeddings/base.py`, ADR-0006) with `GeminiEmbeddingProvider` and `MockEmbeddingProvider` implementations.

## Known limitations (Phase 3)

- **No reranker** — intentionally not implemented (MVP = RRF; revisit only if evaluation shows RRF insufficient, docs/rag-architecture.md §4).
- **No dependency-relationship retrieval leg** — the evidence-id interface exists, but the dependency contribution is empty until the Roslyn analyzer populates it (Phase 4).
- **No analysis persistence** — `analysis_runs` and result tables are Phase 4 (backend).
- **Incident investigation** (`/internal/v1/analysis/incident`) is Phase 4.
- **Structured incident fields, OpenAPI/JSON/YAML chunkers, identifier-aware tokenizer** for the keyword leg — deferred to Phase 4; unknown document types fall back to heading sections / one file chunk today.
- **No measured retrieval accuracy** — the golden dataset (`data/golden-dataset/cases.json`) defines targets; the evaluation runner lands in Phase 7.
- The `app` schema is never touched by this service (ADR-0003).

## Key references

- Boundary: [docs/ai-service-boundary.md](../docs/ai-service-boundary.md)
- LLM design: [docs/llm-integration.md](../docs/llm-integration.md)
- Decisions: [ADR-0002](../docs/adr/0002-service-boundary.md), [ADR-0005](../docs/adr/0005-llm-provider-abstraction.md), [ADR-0007](../docs/adr/0007-structured-output-schema-validation.md)
