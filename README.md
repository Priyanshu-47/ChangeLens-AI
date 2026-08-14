# ChangeLens AI

**AI-powered Production Change Risk & Incident Intelligence Platform.**

ChangeLens helps engineering teams answer two questions:

1. **Before deployment** — *"What could this code change break?"*
2. **After deployment** — *"Something broke. What changed, what is likely affected, and what evidence supports the possible root cause?"*

It combines source-code analysis, dependency analysis, API contract analysis, historical incident retrieval, semantic search (RAG), structured LLM reasoning, controlled tool use, incident investigation, AI evaluation, and observability — without being a generic chatbot.

## Status

**Phase 0 (Architecture) — complete. Phase 1 (Backend foundation) — complete. Phase 2 (AI service) — complete. Phase 3 (Ingestion + hybrid RAG) — complete.** The backend is a tested ASP.NET Core 10 API against PostgreSQL; the Python FastAPI AI service proves the full `.NET → FastAPI → Gemini` path with schema-validated structured output; and Phase 3 adds a real RAG pipeline: structure-aware chunking (tree-sitter), deterministic + Gemini embeddings, pgvector in the `ai` schema, and RRF hybrid retrieval feeding the analysis endpoint with grounding enforcement (mock providers in tests/local dev; live Gemini behind `GEMINI_API_KEY`). See [docs/development-sequence.md](docs/development-sequence.md) for the plan.

| Phase | Deliverable | Status |
| --- | --- | --- |
| 0 | Architecture, ADRs, domain model, API contract, repo structure | ✅ Complete |
| 1 | ASP.NET Core API + PostgreSQL + EF Core foundation | ✅ Complete |
| 2 | FastAPI AI service + Gemini provider | ✅ Complete |
| 3 | Ingestion, chunking, embeddings, pgvector, hybrid retrieval | ✅ Complete |
| 4 | Change analysis workflow | ⏳ Pending |
| 5 | React UI | ⏳ Pending |
| 6 | Agent tools + tool tracing | ⏳ Pending |
| 7 | Evaluation framework + golden dataset | ⏳ Pending |
| 8 | Observability + security hardening | ⏳ Pending |
| 9 | Docker + CI/CD | ⏳ Pending |
| 10 | AWS deployment (only after local MVP is stable) | ⏳ Pending |

## Architecture at a glance

```
React (TypeScript SPA)
   ↓ REST /api/v1 (JWT)
ASP.NET Core 10 Web API   ← orchestration, domain, persistence, auth, audit
   ↓ REST /internal/v1
Python FastAPI AI Service ← ingestion, embeddings, hybrid retrieval, LLM reasoning
   ↓
Gemini API
```

One PostgreSQL instance hosts two logical schemas: the **app** schema (relational domain data, owned by EF Core) and the **ai** schema (documents, chunks, embeddings, owned by the AI service), with **pgvector** for vector search.

## Repository layout

```
changelens-ai/
├── backend/        ASP.NET Core 10 Web API (+ tests)
├── ai-service/     Python FastAPI AI service (+ tests)
├── frontend/       React + TypeScript SPA
├── docker/         Compose files and Dockerfiles
├── docs/           Architecture docs, ADRs, API contract, etc.
└── data/           Demo dataset + golden evaluation dataset (seeded)
```

## Documentation

| Document | Contents |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Final architecture, diagrams, workflows, key decisions |
| [docs/repository-structure.md](docs/repository-structure.md) | Layout and per-folder rationale |
| [docs/mvp-scope.md](docs/mvp-scope.md) | MVP scope, user stories, explicit non-goals |
| [docs/domain-model.md](docs/domain-model.md) | Entities, ER diagram, schema ownership |
| [docs/api-contract.md](docs/api-contract.md) | REST conventions, endpoint catalog, DTOs, async job pattern |
| [docs/ai-service-boundary.md](docs/ai-service-boundary.md) | AI service responsibilities and internal API |
| [docs/rag-architecture.md](docs/rag-architecture.md) | Chunking, embeddings, hybrid retrieval, reranking |
| [docs/llm-integration.md](docs/llm-integration.md) | Gemini integration, provider abstraction, cost control |
| [docs/security-model.md](docs/security-model.md) | AuthN/AuthZ, prompt injection defense, secrets, audit |
| [docs/deployment-strategy.md](docs/deployment-strategy.md) | $0-first local Docker, free tiers, AWS path |
| [docs/development-sequence.md](docs/development-sequence.md) | Phase-by-phase plan with exit criteria |
| [docs/risks-and-tradeoffs.md](docs/risks-and-tradeoffs.md) | Risk register, trade-offs, open questions |
| [docs/definition-of-done.md](docs/definition-of-done.md) | Definition of Done for the MVP |
| [docs/adr/](docs/adr/) | Architecture Decision Records (12) |

## Development principles

- **Correct architecture > quantity of code > number of AI features.**
- **$0-first portfolio project.** Local Docker, PostgreSQL + pgvector, Gemini free tier. No paid infrastructure in the MVP.
- **Deterministic by default.** LLMs are used for reasoning, evidence synthesis, hypothesis generation, and test-scenario generation — never for parsing, dependency calculation, file-type checks, or database queries.
- **Evidence > claims.** Every major AI conclusion references evidence the user can inspect. No ungrounded assertions.
- **No fabricated metrics.** Evaluation dashboards show only results from actual evaluation runs.
- **Structured AI output.** Main analysis results are schema-validated JSON, never uncontrolled prose.
- **Untrusted data.** Repository contents, logs, and incidents are data, never instructions.

## Getting started

1. Copy `.env.example` to `.env` and fill in the placeholders (at minimum the internal key; add `GEMINI_API_KEY` for real LLM calls).
2. Start the stack — PostgreSQL plus the two services:
   ```bash
   docker compose up -d          # postgres + ai-service + backend
   # or without Docker: bash scripts/start-local-postgres.sh, then run the services directly
   # (see backend/README.md and ai-service/README.md)
   ```
3. Apply migrations (first run) and verify:
   ```bash
   cd backend
   dotnet ef database update --project src/ChangeLens.Infrastructure --startup-project src/ChangeLens.Api
   cd src/ChangeLens.Api && dotnet run   # http://localhost:5000/swagger
   # AI service: the ai schema migration runs automatically in Docker (alembic upgrade head);
   # natively: cd ai-service && DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens" \
   #   .venv/Scripts/python -m alembic upgrade head
   ```
4. Seed the demo corpus (optional but recommended):
   ```bash
   cd ai-service
   DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens" \
   EMBEDDING_PROVIDER=mock AI_PROVIDER=mock INTERNAL_API_KEY=change-me-internal-key \
     .venv/Scripts/python scripts/seed_demo.py
   ```
5. Run the tests:
   ```bash
   cd backend && dotnet test tests/ChangeLens.UnitTests
   CHANGELENS_TEST_CONNECTION_STRING="…test connection string…" dotnet test tests/ChangeLens.Api.IntegrationTests
   cd ../ai-service && .venv/Scripts/python -m pytest -q   # 88 tests, zero Gemini calls, no DB needed
   TEST_DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens_test" \
     .venv/Scripts/python -m pytest tests/test_db_integration.py -q   # pgvector integration tests
   ```

Full instructions: [backend/README.md](backend/README.md), [ai-service/README.md](ai-service/README.md). The four-service `docker compose up` (adding the React frontend) is a Phase 9 goal — the Phase 3 compose already runs postgres (pgvector image) + ai-service + backend.
