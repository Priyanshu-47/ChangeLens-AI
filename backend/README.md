# backend — ChangeLens API (ASP.NET Core 10)

> **Phase 1 — complete.** ASP.NET Core 10 Web API foundation: Identity + JWT auth, project-level authorization, projects/repositories/services/incidents, audit log, health checks, Swagger/OpenAPI, EF Core migrations against PostgreSQL, 103 tests.

The orchestrator of ChangeLens AI: it owns authentication, the domain model, workflow orchestration (later phases), persistence (`app` schema), and the audit trail. It never calls Gemini directly — the Python AI service (Phase 2+) is its only AI-facing dependency.

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

The `app` schema is owned by EF Core; the future `ai` schema is owned by the Python AI service (ADR-0003).

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

Tests never call external AI services; the LLM/Gemini integration is Phase 2+.

## API surface (Phase 1)

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
| GET | `/health`, `/api/v1/health` | Liveness / full health (unauthenticated) |

Errors use the uniform envelope: `{ type, title, status, detail, traceId, code }`. See [docs/api-contract.md](../docs/api-contract.md).

## Current phase

Phase 1 of 10. See [docs/development-sequence.md](../docs/development-sequence.md) for what lands in Phase 2 (FastAPI AI service + Gemini provider). Not yet implemented: anything AI/RAG related, pull requests/change analysis, deployments, React UI, Docker images for the API.
