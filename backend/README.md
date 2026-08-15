# backend — ChangeLens API (ASP.NET Core 10)

> **Phase 5 — complete (incident investigation + async analysis).** ASP.NET Core 10 Web API foundation: Identity + JWT auth, project-level authorization, projects/repositories/services/incidents, audit log, health checks, Swagger/OpenAPI, EF Core migrations against PostgreSQL — plus a typed client for the Python AI service and the `POST /api/v1/analyses/change-risk` slice, which since Phase 3 is **RAG-fed** (AI service auto-retrieval) and since Phase 4 runs the **change-intelligence engine**: a Roslyn analyzer + dependency graph, symbol-level change analysis with impact traversal (API + external-integration impact), a safe local-git change source, and `analysis_runs` persistence. Phase 5 adds the **async job system** (bounded in-process queue + background worker, `Queued → Running → Succeeded | Failed` state machine, idempotency keys, retries, timeouts) and the **incident investigation workflow** (`POST /incidents/{id}/investigate` → `202` + `GET /analyses/{id}` polling). 195 tests (144 unit + 51 integration).

The orchestrator of ChangeLens AI: it owns authentication, the domain model, workflow orchestration (later phases), persistence (`app` schema), and the audit trail. It never calls Gemini directly — the Python AI service is its only AI-facing dependency (`.NET → FastAPI → Gemini`).

## Purpose

Provide the REST API foundation for the product: user accounts and roles, project isolation, code-model metadata (repositories, services), incident records with timeline events, and an append-only audit trail — ready to receive the AI/RAG pipeline in later phases.

## Prerequisites

- .NET SDK **10.x** (`dotnet --list-sdks`)
- PostgreSQL 17+ with pgvector **or** Docker (see below)

### Database options

| Option | Command | Notes |
| --- | --- | --- |
| **Docker (primary)** | `docker compose up -d` at the repo root | Postgres 18 + pgvector on port 5432 |
| **No Docker (script)** | `bash scripts/start-local-postgres.sh` | Uses installed PostgreSQL binaries; port **5433**; data in `pgdata/local-dev` |

When using the script, tell the API where the database is:

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=changelens;Username=changelens;Password="
```

## Environment variables

Phase 1 uses ASP.NET Core configuration: `appsettings.json` + `appsettings.Development.json` (dev-only placeholders) overridden by environment variables. The full contract lives in [`.env.example`](../.env.example).

| Variable | Required | Default (Development) | Notes |
| --- | --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | yes | localhost:5432 changelens DB | Use `Host=…;Port=…;Database=changelens;Username=…;Password=…` |
| `Jwt__Issuer` / `Jwt__Audience` | yes | `changelens-dev` / `changelens-client` | |
| `Jwt__SigningKey` | yes | dev placeholder | **Must be a real secret outside Development** — startup fails otherwise |
| `Jwt__ExpiryMinutes` | no | 480 | |
| `Ai__BaseUrl` | yes (analyses) | `http://localhost:8000` in Development | Python AI service base URL (compose: `http://ai-service:8000`) |
| `Ai__ApiKey` | yes (analyses) | dev placeholder | Internal shared key sent as `X-Internal-Key` — **must be a real secret outside Development** |
| `Ai__TimeoutSeconds` | no | 120 | Backend-side HTTP timeout for one analysis call |
| `Analysis__QueueCapacity` | no | 100 | Bounded async job queue capacity (Phase 5) |
| `Analysis__MaxConcurrency` | no | 2 | Max concurrent AI analyses — caps free-tier spend |
| `Analysis__JobTimeoutSeconds` | no | 600 | Per-job timeout; a run can never stay Running forever |
| `Analysis__MaxRetries` | no | 2 | Transient AI failure retries (429/504/502 only; bounded backoff) |
| `Analysis__RetryBackoffSeconds` | no | 5 | Base backoff (exponential, capped at 30s) |
| `Analysis__RecoverOnStartup` | no | `true` | Mark interrupted Running runs failed + re-enqueue Queued runs |
| `Seed__Enabled` | no | `true` in Development | Seeds roles + demo users |

Demo users (Development seed, dev-only passwords):

| Email | Password | Role |
| --- | --- | --- |
| `admin@changelens.dev` | `AdminPass!2026` | Admin (global) |
| `engineer@changelens.dev` | `EngineerPass!2026` | Engineer (global) |
| `viewer@changelens.dev` | `ViewerPass!2026` | Viewer (global, read-only) |

## Database setup (migrations)

```bash
cd backend
dotnet ef migrations list          # shows 20260814171123_InitialCreate
dotnet ef database update          # applies pending migrations to the configured DB
```

The `app` schema is owned by EF Core; the `ai` schema (documents, chunks, embeddings, pgvector) is owned by the Python AI service (ADR-0003) and migrated by Alembic on that side — never by EF.

## Run

```bash
cd backend/src/ChangeLens.Api
dotnet run            # http://localhost:5000
```

- Swagger UI: <http://localhost:5000/swagger> (JWT: login/register → paste token via the Authorize button)
- OpenAPI document: <http://localhost:5000/swagger/v1/swagger.json>
- Health (liveness): `GET /health`
- Health (full, incl. database): `GET /api/v1/health`

## Test

```bash
cd backend
dotnet test tests/ChangeLens.UnitTests
```

Integration tests run against a **real PostgreSQL**:

```bash
# With Docker: uses Testcontainers automatically (pgvector/pgvector:pg18)
dotnet test tests/ChangeLens.Api.IntegrationTests

# Without Docker: point at an existing instance (e.g. the local script's DB)
CHANGELENS_TEST_CONNECTION_STRING="Host=localhost;Port=5433;Database=changelens_test;Username=changelens;Password=" \
  dotnet test tests/ChangeLens.Api.IntegrationTests
```

Tests never call external AI services: unit tests stub the AI client, integration tests replace it with a deterministic fake — Gemini is never contacted by the test suite.

## AI service integration (Phase 2 vertical slice)

The backend talks to the Python AI service through `IAiServiceClient` (port) → `AiServiceClient` (Infrastructure, typed `HttpClient`). It sends the internal key, the `X-Contract-Version: 1` header, and the correlation id (incoming `X-Correlation-ID` or generated), enforces the HTTP timeout, and maps AI error envelopes to typed exceptions (429 → `llm_rate_limited`, 504 → `ai_timeout`, 422 → `ai_validation_failed` with upstream details, else 502). Start the AI service first (mock or Gemini — see [ai-service/README.md](../ai-service/README.md)), then:

```bash
cd ai-service && AI_PROVIDER=mock INTERNAL_API_KEY=change-me-internal-key .venv/Scripts/python -m uvicorn app.main:app --port 8000
cd backend/src/ChangeLens.Api && AI__ApiKey=change-me-internal-key dotnet run
# POST /api/v1/analyses/change-risk with a JWT → validated risk report + usage metadata
```

## API surface (Phase 1 + Phase 2 slice)

| Method | Path | Notes |
| --- | --- | --- |
| POST | `/api/v1/auth/register` | Creates an Engineer user, returns JWT |
| POST | `/api/v1/auth/login` | Returns JWT |
| GET | `/api/v1/auth/me` | Current user + project memberships |
| POST | `/api/v1/projects` | Create (creator becomes Owner) — Admin/Engineer only |
| GET | `/api/v1/projects` | Member's projects (paged) |
| GET/PATCH | `/api/v1/projects/{projectId}` | Detail / update (Owner/Admin) |
| POST/DELETE | `/api/v1/projects/{projectId}/members` | Manage members (Owner/Admin) |
| POST/GET | `/api/v1/projects/{projectId}/repositories` | Register / list repositories |
| GET | `/api/v1/repositories/{repositoryId}` | Repository detail |
| POST/GET | `/api/v1/projects/{projectId}/services` | Create / list services |
| GET | `/api/v1/services/{serviceId}` | Service detail |
| POST/GET | `/api/v1/incidents` | Create (with optional timeline events) / list (filters) |
| GET/PATCH | `/api/v1/incidents/{incidentId}` | Detail incl. timeline / update |
| POST | `/api/v1/incidents/{incidentId}/events` | Append a timeline event |
| POST | `/api/v1/incidents/{incidentId}/investigate` | **Phase 5:** submit an async incident investigation (Engineer+) → `202 { analysisId, status, statusUrl }`; optional `requestId` for idempotency |
| GET | `/api/v1/analyses/{analysisId}` | **Phase 5:** poll a job — `Queued/Running/Succeeded/Failed`; validated result only when Succeeded; safe `error { code, message }` when Failed |
| GET | `/api/v1/audit-logs?projectId=` | Audit trail (Owner/Admin) |
| POST | `/api/v1/analyses/change-risk` | **Phase 2/4 slice:** change-risk analysis via the AI service (Engineer+), synchronous 200 |
| GET | `/health`, `/api/v1/health` | Liveness / full health (unauthenticated) |

Errors use the uniform envelope: `{ type, title, status, detail, traceId, code }` (+ `details` for AI validation failures). See [docs/api-contract.md](../docs/api-contract.md).

## Current phase

Phase 5 of 11. See [docs/development-sequence.md](../docs/development-sequence.md). Phase 3 (hybrid RAG) and Phase 5 (incident investigation) live in the Python AI service — see [ai-service/README.md](../ai-service/README.md). Not yet implemented: React UI (Phase 6), agent tools (Phase 7), evaluation (Phase 8), observability/rate limiting (Phase 9), CI/CD (Phase 10).

## Async analysis jobs (Phase 5)

`POST /api/v1/incidents/{incidentId}/investigate` → `202 Accepted` → background worker → `GET /api/v1/analyses/{analysisId}`. Details:

- **Queue:** bounded in-process `Channel` (`AnalysisJobQueue`); no Redis/Kafka. A full queue persists the run as `Failed(QUEUE_FULL)` — never silently dropped.
- **Worker:** `AnalysisWorker` (`BackgroundService`), concurrency = `Analysis:MaxConcurrency` (default 2), graceful shutdown, per-job DI scope.
- **States:** `Queued → Running → Succeeded | Failed` enforced by `AnalysisRun.TransitionTo`; invalid transitions throw.
- **Retries:** transient AI failures (429/504/502) only, bounded exponential backoff; 422 validation failures never retried.
- **Timeout:** `Analysis:JobTimeoutSeconds` (default 600s) — a run can never stay Running forever (`JOB_TIMEOUT`).
- **Idempotency:** a client `requestId` reuses an outstanding run for the same project; a new investigation after a terminal state starts a fresh run.
- **Persistence:** result stored as JSONB (`analysis_runs.ResultJson`, schema `incident-v1`), with model, prompt version, retrieval snapshot, failure code/message, and queued/started/completed timestamps.
- **Audit:** `AnalysisRequested` / `AnalysisStarted` / `AnalysisCompleted` / `AnalysisFailed`.
- **Known Gemini limitation:** the real text provider rejects the current structured-output schema (HTTP 400) — the same `api_safe_schema` issue documented at the end of Phase 4's validation. Phase 5 tests use `MockAIProvider`; live Gemini incident analysis is not claimed to work until that is resolved.
