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
├── docker/                   ← compose files, Dockerfiles (Phase 1/9)
├── data/                     ← demo dataset + golden evaluation dataset (Phase 3/7)
├── .github/workflows/        ← CI/CD (Phase 10)
└── scripts/                  ← dev helpers (seed, re-index, eval-run)
```

## Decisions and rationale

1. **Monorepo, three deployable units.** One repo means cross-cutting changes (API contract bumps, prompt changes, schema changes) land in one PR with one CI pipeline — the right shape for a solo portfolio project and for keeping the .NET ↔ Python contract in sync. ADR-0001.
2. **.NET solution is deliberately lean — four projects, not a full clean-architecture onion.** `Domain` (entities + invariants, no dependencies), `Application` (services + DTOs + orchestration), `Infrastructure` (EF Core, Roslyn, HTTP client, auth plumbing), `Api` (composition root). This gives testable seams without ceremony. "Clean architecture principles where appropriate" — not abstraction for its own sake.
3. **Python package layout mirrors capability, not layers**: `chunking`, `embeddings`, `retrieval`, `llm`, `evaluation` are the five capabilities in the architecture doc. The `llm` package contains prompt files (versioned) next to the provider code so prompt regressions are tracked in git.
4. **Generated API types for the frontend.** The SPA consumes the backend's OpenAPI document to generate typed clients (`openapi-typescript` or similar), so the contract is a single source of truth and drift is caught in CI.
5. **`data/` holds both the demo dataset and the golden evaluation dataset.** Demo data makes `docker compose up` show a working product immediately; the golden dataset (changes, incidents, expected impacted components, expected related incidents, expected tests) is the input to evaluation runs and is versioned like code.
6. **Tests sit next to their unit**, and integration tests use Testcontainers (real PostgreSQL) with a mocked AI service — Gemini is never consumed in unit tests.

## Conventions

- Backend: C# 13+, file-scoped namespaces, async APIs with cancellation tokens, typed DTOs, structured logging (`ILogger`), no shared mutable state.
- AI service: Python 3.12+, Pydantic v2 models everywhere on the boundary, `ruff` + `mypy`, `pytest`.
- Frontend: TypeScript strict, functional components, no global state library beyond React Query-style hooks unless a real need appears.
- Naming: REST resources plural (`/projects`, `/incidents`); DB tables snake_case; code identifiers follow each language's convention.
