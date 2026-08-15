# ChangeLens AI

**AI-powered production change risk & incident intelligence platform.**

ChangeLens answers the two questions engineering teams ask around every production change:

1. **Before deployment** — *"What could this code change break?"*
2. **After deployment** — *"Something broke. What changed, what is likely affected, and what evidence supports the root cause?"*

It combines source-code analysis (Roslyn), dependency-graph impact analysis, hybrid RAG over historical incidents + runbooks + code, structured LLM reasoning, a *controlled* AI tool loop, async incident investigation, deterministic evaluation, and full AI traceability — a production-shaped system, not a chatbot.

## Problem

Post-incident reviews are slow because evidence is scattered: git history, deployment logs, runbooks, past incidents, and source code live in different tools with different vocabularies. Risk analysis before a deploy is usually a manual reading of a diff. ChangeLens treats this as an engineering problem: a change model → a dependency graph → grounded evidence retrieval → schema-validated, evidence-cited analysis — with the system explicitly saying what it **does not know**.

## Why I Built This

I wanted to demonstrate production-grade AI application engineering end-to-end — not another chatbot or a thin RAG wrapper. ChangeLens is deliberately opinionated about where AI is and isn't used: LLMs handle reasoning and evidence synthesis over **structured, validated contracts**, while deterministic systems (Roslyn, the dependency graph, retrieval ranking, grounding, evaluation) do the work that must be exact. Every AI conclusion is traceable to evidence the user can open, and the system is honest about what it cannot measure (the evaluation section labels synthetic/mock results as such, and the live Gemini limitation is documented rather than hidden). The whole platform runs at **$0** on local Docker + PostgreSQL + mock providers, with CI to match.

## Why this matters (for a portfolio)

- Two complete production workflows, both grounded and auditable.
- **Controlled agents, not multi-agent theater**: the AI proposes tool calls; the application validates, authorizes, executes, and audits them. No arbitrary SQL/shell/URL/write tools exist.
- **Deterministic evaluation** against a versioned 20-case golden dataset with per-leg retrieval ablation — measured, honest numbers, no LLM-as-judge, no fabricated metrics.
- **$0-first**: runs entirely on local Docker + PostgreSQL + pgvector + the Gemini free tier; CI is mock-based and free.

## Architecture

```mermaid
flowchart TB
    FE["React SPA (Vite + TS)"] -->|"REST /api/v1 · JWT"| BE
    BE["ASP.NET Core 10<br/>authz · domain · orchestration · audit"] -->|"REST /internal/v1 · X-Internal-Key"| AI
    BE --> PG[("PostgreSQL + pgvector<br/>app schema (EF) + ai schema (Alembic)")]
    AI["Python FastAPI<br/>prompts · structured output · grounding · hybrid RAG"] --> PG
    AI --> G["Gemini API<br/>(provider abstraction — mock for $0)"]
```

**Workflow A — Change Risk:** code change → Roslyn symbol analysis → dependency graph → hybrid retrieval (vector + keyword + dependency → RRF) → grounded risk report.

**Workflow B — Incident Investigation:** incident → `202` async job → normalized context → hybrid retrieval → **controlled tool loop** → grounded root-cause candidates + remediation + unknowns.

## Key capabilities

| Capability | What it is |
| --- | --- |
| Change intelligence | Roslyn analyzer + in-memory dependency graph in .NET; symbol-level change analysis with bounded impact traversal (safe local-git change source — never executes repo code) |
| Hybrid RAG | Structure-aware chunking (tree-sitter for code, heading-aware for incidents/runbooks), pgvector cosine + PostgreSQL FTS + metadata filters + dependency leg, merged with RRF; hard project isolation in every SQL statement |
| Structured AI output | Pydantic-validated results with bounded repair; deterministic grounding rule (every cited evidence ID must exist; empty/unknown ids rejected); layered prompt architecture that treats evidence as untrusted data |
| Async analysis | `POST /incidents/{id}/investigate` → `202` → bounded in-process queue + background worker (concurrency-capped, cancellable, retries only transient failures) → `GET /analyses/{id}` polling; explicit job state machine |
| Controlled AI tools | AI proposes; .NET validates/authorizes/executes/audits. Seven read-only, project-isolated tools (`get_incident`, `get_incident_timeline`, `get_service`, `get_runbook`, `get_source_symbol`, `get_dependency_paths`, `search_evidence`); bounded loop, per-tool timeouts, tool-call trace |
| Evaluation | Versioned 20-case golden dataset runner (`python -m app.evaluation.run`, mock providers, zero Gemini): Recall@K / Precision@K / MRR / Hit Rate per leg, grounding + schema checks, per-case tool trace, JSON/Markdown reports, baseline comparison |
| Observability | Per-analysis trace (`analysis_runs.TraceJson`): real stage timings (Context / Roslyn / Dependency Graph / AI / Persistence), retrieval-leg attribution, tool calls, normalized failure categories; `GET /analyses/{id}/trace`; React trace panel + retrieval explorer |
| Security | JWT + RBAC + project-level authorization (404 for invisible resources), cross-project isolation tested end-to-end, prompt-injection defense (evidence is data, never instructions), no secrets in logs, append-only audit trail, controlled CORS, in-memory rate limiting |

## Tech stack

React 18 + TypeScript + Vite · ASP.NET Core 10 · FastAPI (Python 3.12+) · PostgreSQL + pgvector · Entity Framework Core + Alembic · Roslyn · tree-sitter · Gemini (provider abstraction) · GitHub Actions

## Repository structure

```
frontend/    React SPA (login, projects, incidents, analyses, change risk, trace)
backend/     ASP.NET Core 10 (domain, authz, orchestration, audit, Roslyn, tool execution)
ai-service/  FastAPI (providers, prompts, structured output, hybrid RAG, evaluation runner)
data/        demo corpus (AcmePay repo, 20 incidents, 5 runbooks) + golden dataset (v1)
docs/        architecture, ADRs (12), API contract, evaluation, security, agent tools, costs,
             demo script, interview prep, resume bullets, future roadmap
scripts/     local PostgreSQL helpers
.github/     $0 CI workflow (backend, Python, frontend, evaluation, secret scan)
```

## Quick start (Docker — full stack)

Prerequisites: Docker (with Compose). Everything else is containerized.

```bash
git clone <repo> && cd changelens-ai
cp .env.example .env            # fill INTERNAL_API_KEY + JWT_SIGNING_KEY (any strong values locally)
docker compose up -d --build    # postgres + ai-service + backend + frontend

# Seed the demo corpus (mock embeddings — deterministic, $0, idempotent):
cd ai-service
DATABASE_URL="postgresql+psycopg://changelens:changelens_dev_password@localhost:5432/changelens" \
EMBEDDING_PROVIDER=mock ./.venv/Scripts/python scripts/seed_demo.py
```

Open **http://localhost:8080** and log in with a **development-only** seeded account:

| Role | Email | Password |
| --- | --- | --- |
| Admin | `admin@changelens.dev` | `AdminPass!2026` |
| Engineer | `engineer@changelens.dev` | `EngineerPass!2026` |
| Viewer | `viewer@changelens.dev` | `ViewerPass!2026` |

> These credentials exist only in local `Development` seeding — never use them outside a dev/demo environment.

No Docker? `scripts/start-local-postgres.sh` starts a project-local PostgreSQL; then run the backend, AI service, and frontend natively per their READMEs. Full instructions: [backend/README.md](backend/README.md), [ai-service/README.md](ai-service/README.md), [frontend/README.md](frontend/README.md). Walk through the whole product in ~5 minutes with [docs/demo-script.md](docs/demo-script.md).

## Tests

```bash
cd backend && dotnet test                                    # 187 unit tests
CHANGELENS_TEST_CONNECTION_STRING="Host=localhost;Port=5433;Database=changelens_test;Username=changelens" \
  dotnet test tests/ChangeLens.Api.IntegrationTests          # 53 integration tests (real PostgreSQL)
cd ../ai-service && ./.venv/Scripts/python -m pytest -q      # 157 unit tests — zero Gemini, no DB
TEST_DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens_test" \
  ./.venv/Scripts/python -m pytest tests/test_db_integration.py -q   # 12 pgvector integration tests
cd ../frontend && npm test && npm run build                  # 34 tests + production build
```

CI (`.github/workflows/ci.yml`) runs all of this at **$0** with mock providers and a PostgreSQL service container — no Gemini key, no AWS.

## Evaluation results (measured, mock providers — synthetic corpus)

Runner: `cd ai-service && DATABASE_URL="…" ./.venv/Scripts/python -m app.evaluation.run`. Report: `data/evaluation-output/evaluation-report.md` (gitignored).

- **20/20 cases evaluated** (dataset `v1`) · schema-valid 20/20 · grounded 20/20
- Retrieval (Recall@5 / Recall@10 / MRR):
  - vector **0.625 / 0.679 / 0.975** · keyword **0.529 / 0.546 / 1.000** · hybrid **0.583 / 0.617 / 1.000**
  - dependency leg scores 0 on retrieval-style queries (it is change-model-driven — reported, not hidden)
- Tool loop: 20/20 loops completed, 40/40 proposals valid, 20/20 grounding after tools

Honest caveats: these are **synthetic-corpus + mock-embedding results that prove the framework**, not production accuracy; hybrid does not beat vector alone on this dataset — the ablation is the point (see [docs/evaluation.md](docs/evaluation.md)).

## Known limitations

- **Live Gemini structured output**: the configured `gemini-3.1-flash-lite` currently rejects the project's structured-output schema with HTTP 400. The provider abstraction is intact and the live path was code-fixed and smoke-tested in earlier phases; normal tests/CI/eval all run with the deterministic mock provider. **Live Gemini analysis is not claimed to work.**
- No reranker; no LLM-as-judge (deliberate — deterministic evaluation first).
- Async queue and rate limiter are in-process (single instance by design).
- Docker Compose is statically validated in the authoring environment; `docker compose up --build` is the explicit next verification on a Docker-equipped machine.
- No hosted deployment (local Docker is the official MVP; see [docs/deployment-strategy.md](docs/deployment-strategy.md)).

## Screenshots

Not captured in this environment (no browser tooling) — the UI is fully described in [docs/demo-script.md](docs/demo-script.md) and covered by 34 component tests (login, project dashboard, incident detail, async investigation, analysis result with evidence linking, grounding badge, unknowns, change-risk, trace + tool calls). An honest placeholder and a capture how-to live in [docs/screenshots/README.md](docs/screenshots/README.md); no mockups are published.

## Documentation

| Document | Contents |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Final architecture, diagrams, workflows, key decisions |
| [docs/repository-structure.md](docs/repository-structure.md) | Layout and per-folder rationale |
| [docs/mvp-scope.md](docs/mvp-scope.md) | MVP scope, user stories, explicit non-goals |
| [docs/domain-model.md](docs/domain-model.md) | Entities, ER diagram, schema ownership |
| [docs/api-contract.md](docs/api-contract.md) | REST conventions, endpoint catalog, DTOs, async job pattern |
| [docs/ai-service-boundary.md](docs/ai-service-boundary.md) | AI service responsibilities and internal API |
| [docs/rag-architecture.md](docs/rag-architecture.md) | Chunking, embeddings, hybrid retrieval, RRF |
| [docs/llm-integration.md](docs/llm-integration.md) | Gemini integration, provider abstraction, cost control |
| [docs/security-model.md](docs/security-model.md) | AuthN/AuthZ, prompt injection defense, secrets, audit, production checklist |
| [docs/deployment-strategy.md](docs/deployment-strategy.md) | $0-first local Docker, free tiers, AWS path |
| [docs/evaluation.md](docs/evaluation.md) | Metrics, dataset, runner, trace architecture, limitations |
| [docs/agent-tools.md](docs/agent-tools.md) | Tool loop: registry, safety, trace, audit, evaluation |
| [docs/costs.md](docs/costs.md) | Cost model — local $0, Gemini free tier, optional AWS |
| [docs/demo-script.md](docs/demo-script.md) | 3–5 minute interview demo |
| [docs/development-sequence.md](docs/development-sequence.md) | Phase-by-phase plan with exit criteria |
| [docs/risks-and-tradeoffs.md](docs/risks-and-tradeoffs.md) | Risk register, trade-offs, open questions |
| [docs/definition-of-done.md](docs/definition-of-done.md) | Definition of Done for the MVP |
| [docs/adr/](docs/adr/) | Architecture Decision Records (12) |

## Future roadmap

Categorized as CURRENT / NEXT / FUTURE in [docs/future-roadmap.md](docs/future-roadmap.md): live Gemini schema compatibility, real-embedding evaluation, hosted evaluation dashboard, GitHub integration, and (only with measured need) a reranker — plus the explicit non-goals (multi-agent, arbitrary tools, distributed infrastructure, LLM-judge as primary evaluation).

## Status

**Phases 0–10 complete.** The engineering project is feature-complete; Docker installation was attempted and blocked in the authoring environment (details in the Phase 10 report), so `docker compose up --build` on a Docker-equipped machine is the one outstanding verification item. Portfolio release docs: [docs/interview-prep.md](docs/interview-prep.md), [docs/resume-bullets.md](docs/resume-bullets.md), [docs/project-description.md](docs/project-description.md). Phase 9 delivered production hardening + deployment readiness: full four-service Docker Compose (frontend nginx container, DB-gated healthchecks, non-root everywhere), controlled CORS, in-memory rate limiting, non-dev secret validation, $0 GitHub Actions CI (including the evaluation runner), and per-case tool trace in evaluation reports. **Phase 10 (optional AWS) only after the local MVP is stable.**

| Phase | Deliverable | Status |
| --- | --- | --- |
| 0 | Architecture, ADRs, domain model, API contract, repo structure | ✅ |
| 1 | ASP.NET Core API + PostgreSQL + EF Core foundation | ✅ |
| 2 | FastAPI AI service + Gemini provider abstraction | ✅ |
| 3 | Ingestion, chunking, embeddings, pgvector, hybrid retrieval | ✅ |
| 4 | Change intelligence: Roslyn + dependency graph + change-risk pipeline | ✅ |
| 5 | Incident investigation + async analysis jobs (202 + poll) | ✅ |
| 6 | React UI — dashboard, incident investigation, change risk | ✅ |
| 7 | Evaluation + AI observability (runner, trace, retrieval explorer) | ✅ |
| 8 | Controlled AI tools + tool tracing (registry, loop, audit, trace UI) | ✅ |
| 9 | Production hardening + deployment readiness (Docker, CI, security) | ✅ |
| 10 | Final verification + portfolio release | ✅ |
| — | AWS deployment (optional, only after local is stable) | ⏳ |
