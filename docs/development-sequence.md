# Development Sequence

> Phase 0 deliverable. One phase at a time, each with a definition of done. No phase starts before the previous one's exit criteria pass. Ordering follows dependency: data → retrieval → reasoning → UI → tools → evaluation → hardening → ops.

## Phase 0 — Architecture ✅ (this repository)
**Goal:** decisions + contracts before code.
**Deliverables:** architecture doc, repo structure, domain model, API contract, AI service boundary, RAG design, LLM design, security model, deployment strategy, ADRs 0001–0012, `.env.example`, `.gitignore`, stubbed service directories.
**Exit:** all 15 review items from the brief answered and approved.

## Phase 1 — Backend Foundation ✅
**Goal:** a real, runnable ASP.NET Core API on PostgreSQL.
**Deliverables:** .NET 10 solution (Api/Domain/Application/Infrastructure), EF Core + `InitialCreate` migration (app schema: users/Identity, projects, members, repositories, services, incidents, events, audit_logs), auth (Identity + JWT + roles + project-level authorization), health + Swagger/OpenAPI, dev seeding, unit + integration tests (63 + 40 = 103, real PostgreSQL via Testcontainers or a connection-string override).
**Exit:** `dotnet test` green; API runs against local postgres; Swagger documents the Phase-1 endpoints; JWT login works — all verified 2026-08-14. `docker compose up` provides postgres; `scripts/start-local-postgres.sh` covers no-Docker machines.

## Phase 2 — AI Service ✅
**Goal:** FastAPI service with a real Gemini provider and structured output.
**Deliverables:** FastAPI service (`app/`): pydantic-settings config validated at startup, `IAIProvider` protocol + `GeminiProvider` (google-genai, structured outputs via `response_schema`, no deprecated sampling params) + `MockAIProvider` (deterministic, `AI_PROVIDER=mock`), layered versioned prompt (`risk-v1`) with injection sanitizer, structured-output pipeline (Pydantic + bounded repair + safe failure `422 AI_VALIDATION_FAILED`), deterministic grounding rule, retries (429/5xx only, backoff + jitter), `/internal/v1/health/live|ready` + `/health` + `/ready`, internal-key auth + contract-version header + correlation-id middleware + structured JSON logs; Python tests (57, zero Gemini); optional gated live smoke test; Dockerfile (non-root). Backend: `IAiServiceClient` port + `AiServiceClient` (typed HttpClient, correlation, error mapping), `AiOptions`, `POST /api/v1/analyses/change-risk` vertical slice (Engineer+, audited), unit + integration tests (16 new).
**Exit:** FastAPI starts, `/health` + `/ready` + Swagger work; mock analysis returns a validated grounded result end-to-end (`.NET → FastAPI → mock`); 125 .NET tests + 57 Python tests green; live Gemini smoke test available behind `RUN_GEMINI_TESTS=true`; normal suite and startup use zero Gemini calls — all verified 2026-08-14.

## Phase 3 — Ingestion + RAG ✅
**Goal:** documents in, ranked retrieval out.
**Deliverables:** ai schema migrations (documents, chunks, embeddings), structure-aware chunkers (tree-sitter code: C#/JS/TS/Python; heading-aware incidents/runbooks), embedding abstraction (`GeminiEmbeddingProvider` + deterministic `MockEmbeddingProvider`), hybrid retrieval (pgvector cosine + PostgreSQL FTS + metadata filters + RRF) with hard project isolation, `POST /internal/v1/ingest/documents` + `POST /internal/v1/retrieval/search`, idempotent ingestion (content hashing, no re-chunk/re-embed on unchanged content, stale-model re-embed), RAG-fed analysis (auto-retrieve → `chunk:<uuid>` evidence ids → grounding still enforced), demo corpus (`data/demo-repository` AcmePay app that compiles, 20 synthetic incidents, 5 runbooks, 20-case golden dataset) + `scripts/seed_demo.py`, pgvector on local PostgreSQL, readiness reports DB + vector-extension health.
**Exit:** ingest → search returns the expected top hits for demo queries (verified live with the mock embedding provider), project isolation enforced server-side (regression test), re-ingesting unchanged content is a no-op (verified live: 49 documents → 0 chunks on re-run), 88 unit + 8 PostgreSQL integration tests green, zero Gemini calls in the normal suite — all verified 2026-08-15.

## Phase 4 — Change Analysis
**Goal:** both workflows end-to-end, evidence-grounded, structured.
**Deliverables:** Roslyn change parsing + dependency graph + contract extraction in .NET; evidence-package assembly; `/analysis/risk` + `/analysis/incident` wiring; risk report + investigation persistence with evidence items; async job runner (202 + poll); deterministic preprocessing before any LLM call.
**Exit:** Workflow A and B succeed on demo data; every conclusion has evidence ids; validation failures surface as `AI_VALIDATION_FAILED`, never prose.

## Phase 5 — React UI
**Goal:** a polished product surface.
**Deliverables:** Vite + React + TS + React Router; shadcn/ui + Tailwind; dashboard; change analysis view (risk factors, evidence panel, recommended tests); incident investigation view (timeline, candidates with evidence/unknowns); dependency graph (React Flow); typed API client generated from OpenAPI.
**Exit:** S1/S2/S3 user stories demonstrable against the running stack; responsive, no placeholder screens.

## Phase 6 — Agent Tools
**Goal:** controlled, audited tool use — not "multi-agent theater".
**Deliverables:** tool registry + schemas in .NET; tool-call proposal loop via AI service; execution with validation/authorization/timeout/retry/audit; `tool_calls` trace in `analysis_runs`; UI toggle to inspect tool activity.
**Exit:** investigation can propose+execute e.g. `search_incidents`, `get_deployment`, `get_logs`; rejected/unauthorized calls are audited; no tool runs without backend authorization.

## Phase 7 — Evaluation
**Goal:** measured, honest numbers.
**Deliverables:** golden dataset (15–25 cases: changes, incidents, expected impacted components, expected related incidents, expected tests); eval runner comparing keyword-only vs vector-only vs hybrid vs full pipeline; metrics (Recall@K, precision, MRR, groundedness, hallucination rate, latency, tokens, estimated cost, schema-validation failures) persisted in `evaluation_runs`; evaluation dashboard; eval-as-regression-gate in CI (Phase 9).
**Exit:** the dashboard displays only real run results; a strategy comparison shows which retrieval mode wins on this dataset.

## Phase 8 — Observability + Security Hardening
**Goal:** production-grade traces and controls.
**Deliverables:** AI run trace view (model, prompt version, retrieval queries/docs, tool calls, tokens, cost, validation/guardrail status); structured logging review; rate limiting; audit-log UI; guardrail pass on all LLM paths; secrets hygiene pass.
**Exit:** any analysis is fully explainable from the trace view; red-team pass on prompt-injection scenarios documented.

## Phase 9 — Docker + CI/CD
**Goal:** `docker compose up` for everyone.
**Deliverables:** four-service compose (frontend/backend/ai-service/postgres) with healthchecks + volumes + non-root; GitHub Actions: lint → unit → integration → security scan → AI evaluation (regression gate) → docker build; README quick-start.
**Exit:** clean machine + `docker compose up` = working demo with seeded data; CI green.

## Phase 10 — AWS (only when local is stable)
**Goal:** modular, cost-estimated deployment.
**Deliverables:** Terraform modules per service; cost estimate reviewed before provisioning; S3/CloudFront frontend; Fargate backend + AI service; managed PostgreSQL decision; SQS optional; Secrets Manager; Cognito seam; CloudWatch; architecture diagrams updated; known-limitations doc extended.
**Exit:** deployed demo meets the cost estimate agreed in advance; local remains the source of truth.

## Sequencing notes
- **Phase 1 and 2 can be built in parallel** (different repos, contract agreed in Phase 0) — this is the only planned parallelism; it shrinks the first milestone.
- The **async job runner (Phase 4)** is built before the UI so the UI never blocks on AI latency.
- **Evaluation data is curated from Phase 3 onward** (retrieval traces are its raw material), even though the runner lands in Phase 7.
- Anything discovered that contradicts these docs produces a doc change first (ADR if architectural), then code.
