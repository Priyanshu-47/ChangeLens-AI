# backend — ChangeLens API (ASP.NET Core 10)

> **Phase 4 — complete (change intelligence).** ASP.NET Core 10 Web API foundation: Identity + JWT auth, project-level authorization, projects/repositories/services/incidents, audit log, health checks, Swagger/OpenAPI, EF Core migrations against PostgreSQL — plus a typed client for the Python AI service and the `POST /api/v1/analyses/change-risk` slice, which since Phase 3 is **RAG-fed** (AI service auto-retrieval) and since Phase 4 runs the **change-intelligence engine**: a Roslyn analyzer + dependency graph, symbol-level change analysis with impact traversal (API + external-integration impact), a safe local-git change source, and `analysis_runs` persistence. 158 tests (111 unit + 47 integration).

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
| GET | `/api/v1/audit-logs?projectId=` | Audit trail (Owner/Admin) |
| POST | `/api/v1/analyses/change-risk` | **Phase 2 slice:** change-risk analysis via the AI service (Engineer+). Synchronous 200 today; becomes 202 + poll in Phase 4 |
| GET | `/health`, `/api/v1/health` | Liveness / full health (unauthenticated) |

Errors use the uniform envelope: `{ type, title, status, detail, traceId, code }` (+ `details` for AI validation failures). See [docs/api-contract.md](../docs/api-contract.md).

## Current phase

Phase 3 of 10. See [docs/development-sequence.md](../docs/development-sequence.md). Phase 3 (hybrid RAG) lives in the Python AI service — see [ai-service/README.md](../ai-service/README.md). Not yet implemented: change parsing/dependency analysis + async job runner + result persistence (Phase 4), React UI (Phase 5), agent tools (Phase 6), evaluation (Phase 7), observability/rate limiting (Phase 8), CI/CD (Phase 9).
