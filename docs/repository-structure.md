# Repository Structure

> Phase 0 deliverable. Directory READMEs describe each unit; this document explains the layout decisions. Directories beyond `docs/` are stubs until their phase lands.

## Layout

```
changelens-ai/
├── README.md                 ← project overview + phase status
├── .env.example              ← environment variable contract (never real secrets)
├── .gitignore
├── docs/                     ← architecture, ADRs, API contract, domain model (this phase)
│   ├── adr/                  ← ADR-0001 … ADR-0012
│   └── …                     ← see README.md documentation index
├── backend/                  ← ASP.NET Core 10 Web API (Phase 1+)
│   ├── src/
│   │   ├── ChangeLens.Api/           ← controllers, middleware, DI composition, auth
│   │   ├── ChangeLens.Domain/        ← entities, value objects, invariants (no deps)
│   │   ├── ChangeLens.Application/   ← services, use-case orchestration, DTOs, ports, Phase 8 tool registry + read-only tools (Tools/)
│   │   └── ChangeLens.Infrastructure/← EF Core, Roslyn analyzer, HTTP client to AI service
│   └── tests/
│       ├── ChangeLens.UnitTests/     ← domain + service unit tests (mocked AI)
│       └── ChangeLens.Api.IntegrationTests/ ← WebApplicationFactory + Testcontainers
├── ai-service/               ← Python FastAPI AI service (Phase 2+)
│   ├── app/
│   │   ├── api/              ← internal REST endpoints
│   │   ├── chunking/         ← structure-aware chunkers (code, markdown, incidents, OpenAPI)
│   │   ├── embeddings/       ← provider abstraction (gemini / local)
│   │   ├── retrieval/        ← hybrid search, filters, RRF, rerankers
│   │   ├── llm/              ← provider abstraction, prompts, structured output, repair
│   │   ├── evaluation/       ← golden dataset runner + metrics
│   │   └── core/             ← config (pydantic-settings), db, schemas, observability
│   └── tests/                ← unit, retrieval, schema-validation, prompt regression, eval
├── frontend/                 ← React + TypeScript SPA (Phase 6, implemented)
│   ├── src/
│   │   ├── api/              ← typed API client + DTO mirrors (client, endpoints, types)
│   │   ├── auth/             ← AuthContext + ProtectedRoute (JWT, session restore)
│   │   ├── projects/         ← ProjectContext (list/selection; backend stays authoritative)
│   │   ├── pages/            ← Login, Dashboard, Incidents, IncidentDetail, Analyses, Analysis, ChangeRisk
│   │   ├── components/       ← Layout (sidebar/topbar), ui primitives, Timeline, Investigation
│   │   ├── hooks/            ← useAsync, useAnalysisPolling
│   │   ├── styles/           ← global.css design system (no UI framework dependency)
│   │   └── test/             ← setup + fetch-mock helpers
│   └── *.test.{ts,tsx}       ← Vitest + Testing Library (mocked HTTP, zero Gemini)
├── docker-compose.yml        ← full four-service stack (Phase 9): postgres + ai-service + backend + frontend
├── docker/                   ← Docker strategy + README (Dockerfiles live next to each service)
├── backend/Dockerfile        ← multi-stage .NET 10 SDK → non-root runtime (Phase 9)
├── ai-service/Dockerfile     ← python slim, non-root, alembic upgrade + uvicorn
├── frontend/Dockerfile       ← node build → nginx-unprivileged (SPA + /api proxy)
├── data/                     ← demo dataset + golden evaluation dataset (Phase 3/7)
├── .github/workflows/ci.yml  ← $0 CI: backend, python, frontend, evaluation, secret scan (Phase 9)
└── scripts/                  ← dev helpers (start/stop local postgres)
```

## Decisions and rationale

1. **Monorepo, three deployable units.** One repo means cross-cutting changes (API contract bumps, prompt changes, schema changes) land in one PR with one CI pipeline — the right shape for a solo portfolio project and for keeping the .NET ↔ Python contract in sync. ADR-0001.
2. **.NET solution is deliberately lean — four projects, not a full clean-architecture onion.** `Domain` (entities + invariants, no dependencies), `Application` (services + DTOs + orchestration), `Infrastructure` (EF Core, Roslyn, HTTP client, auth plumbing), `Api` (composition root). This gives testable seams without ceremony. "Clean architecture principles where appropriate" — not abstraction for its own sake.
3. **Python package layout mirrors capability, not layers**: `chunking`, `embeddings`, `retrieval`, `llm`, `evaluation` are the five capabilities in the architecture doc. The `llm` package contains prompt files (versioned) next to the provider code so prompt regressions are tracked in git.
4. **Hand-written typed API client for the frontend.** `src/api/` mirrors the backend DTOs (types.ts) with centralized base-URL/auth/correlation-id handling. OpenAPI generation remains a future option; the contract is enforced by the backend integration tests and the component tests that mock the client.
5. **`data/` holds both the demo dataset and the golden evaluation dataset.** Demo data makes `docker compose up` show a working product immediately; the golden dataset (changes, incidents, expected impacted components, expected related incidents, expected tests) is the input to evaluation runs and is versioned like code.
6. **Tests sit next to their unit**, and integration tests use Testcontainers (real PostgreSQL) with a mocked AI service — Gemini is never consumed in unit tests.

## Conventions

- Backend: C# 13+, file-scoped namespaces, async APIs with cancellation tokens, typed DTOs, structured logging (`ILogger`), no shared mutable state.
- AI service: Python 3.12+, Pydantic v2 models everywhere on the boundary, `ruff` + `mypy`, `pytest`.
- Frontend: TypeScript strict, functional components, no global state library beyond React Query-style hooks unless a real need appears.
- Naming: REST resources plural (`/projects`, `/incidents`); DB tables snake_case; code identifiers follow each language's convention.
