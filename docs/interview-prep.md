# Interview Preparation

> Phase 10 deliverable. Every answer below reflects the **actual implementation** in this repository — nothing claimed that isn't built, tested, and measured. Use the [demo script](demo-script.md) to drive the live walkthrough; these answers explain the decisions behind it.

## Why .NET + Python?

Two services with two ownership boundaries, chosen deliberately ([ADR-0002](adr/0002-service-boundary.md), [ai-service-boundary.md](ai-service-boundary.md)):

- **.NET (ASP.NET Core 10)** owns the parts that must be deterministic and strongly typed: users, projects, authorization, incidents, orchestration, audit, and — critically — the **code-analysis engine** (Roslyn symbol model, dependency graph) and the **tool-execution boundary**. C# gives compile-time contracts for the whole domain and the tool registry.
- **Python (FastAPI)** owns the AI-specific parts: provider abstraction, versioned prompts, structured-output validation (Pydantic), and the retrieval service. Python is where the AI ecosystem lives — the Gemini SDK, Pydantic models, pgvector — and iterating on prompts/schemas is fastest there.
- The seam is a typed HTTP contract (`/internal/v1`, `X-Internal-Key` + contract version). Each service is independently testable with mocks; nothing forces both languages except honest boundaries.

## Why PostgreSQL + pgvector?

One database, two logical schemas ([ADR-0003](adr/0003-single-postgres-schema-per-service.md)): the **app** schema (relational domain, EF Core) and the **ai** schema (documents/chunks/embeddings, Alembic). pgvector gives real cosine similarity with HNSW indexing *inside* the same Postgres instance — no separate vector database, no new infrastructure, **$0**. It also keeps project isolation expressible as a plain SQL filter on every retrieval query (defense in depth: the app filters by `project_id` and the AI service filters again).

## Why hybrid RAG (vector + keyword + metadata + dependency)?

- **Vector** (pgvector cosine) finds semantically similar content but is weak on exact technical identifiers.
- **Keyword** (PostgreSQL FTS, `simple` config) matches exact terms — `TimeoutException`, `401`, `JWT`, `PaymentGatewayClient` — which are exactly what incidents are made of.
- **Metadata** filters (document type, service, language, environment) constrain scope.
- **Dependency** (Phase 4) lifts chunks whose *symbols/paths* the change model touched — retrieval that knows the codebase structure, not just the words.
- Merged with **RRF** (reciprocal rank fusion) because vector and keyword scores are not directly comparable; RRF combines ranks, not raw scores. The per-leg attribution is exposed in the trace so the UI explains *why* each result surfaced.

## Why RRF? Why not a learned reranker?

RRF is deterministic, parameter-light (one `k`), and needs no training data or extra cost. The Phase 7 evaluation measures each leg *and* the hybrid so the decision stays evidence-based. A reranker is a documented future option (docs/future-roadmap.md) **only if measured need** justifies it — the honest current finding is that hybrid does **not** beat vector alone on the synthetic/mock dataset, which is exactly why the ablation exists.

## Why Roslyn (in .NET)?

Change risk is about *code*, so the change model is built by real compilation-grade analysis: the Roslyn symbol model gives us added/removed/modified **symbols** (not just changed lines), and a dependency graph of CALLS / REFERENCES_TYPE / IMPLEMENTS / INHERITS edges. Impact traversal (`maxDepth` bounded) answers "what could this change break?" with a structural answer, and feeds the dependency retrieval leg. This is deterministic, testable, and free — the LLM never parses code (project rule: LLMs are for reasoning, not deterministic work).

## Why async jobs (202 + poll) instead of synchronous AI?

AI analysis takes seconds (retrieval + provider + validation). A synchronous endpoint would tie up HTTP connections and force the UI to block. The contract returns **202 Accepted + analysisId** immediately; the client polls `GET /analyses/{id}`. This is the pattern the UI uses ([ADR-0009](adr/0009-async-analysis-jobs.md)).

## Why an in-process queue? Why not Kafka?

The MVP is **$0-first and single-instance**. An in-process `Channel`-based bounded queue + `BackgroundService` worker gives: bounded capacity (queue-full → persisted `Failed(QUEUE_FULL)`, never a silent drop), configurable concurrency (`Analysis:MaxConcurrency=2`), graceful shutdown, retries (transient only), per-job timeouts, and startup recovery of interrupted runs. Kafka/SQS add a broker, ops burden, and latency for zero MVP benefit. The seam is the `AnalysisRun` row in PostgreSQL — moving to a distributed queue later replaces the worker plumbing, not the domain model ([ADR-0009](adr/0009-async-analysis-jobs.md), future-roadmap.md).

## Why single-agent? Why controlled tools instead of multi-agent?

A multi-agent architecture (planner/researcher/analyst critics) multiplies latency, cost, and failure modes without improving the core task: grounded investigation of one incident. ChangeLens uses **one AI reasoning loop** with the *application* owning orchestration: the AI proposes tool calls; .NET validates the name against an allowlist registry, validates arguments, authorizes against the project, executes with a timeout, audits, and feeds sanitized results back — bounded by `Analysis:MaxToolCalls=3` ([ADR-0008](adr/0008-controlled-tool-use.md), agent-tools.md). This is the "AI proposes, application authorizes and executes" pattern — safer and simpler than autonomous agents.

## Why does .NET execute tools, not Python?

Tool execution touches domain truth (incidents, services, runbooks, source symbols, dependency graph) and must enforce authorization and project isolation. That authority lives in .NET ([ADR-0002](adr/0002-service-boundary.md)); Python only *proposes* a tool call in structured form. Python can never access PostgreSQL directly, never execute anything — it has no tool-execution code path at all. Adversarial tests prove unknown tools → `TOOL_NOT_ALLOWED`, cross-project → `NOT_FOUND`, invalid args → `INVALID_ARGUMENT`.

## How is project isolation enforced?

Twice, independently: (1) an authorization handler resolves the project from the authenticated request and checks membership/role — non-members get **404**, not 403, so they can't even probe existence; (2) every data-layer query filters by `project_id`, and the AI service hard-filters every retrieval query by the project id from the *backend*, never from the model. Tool calls derive project scope from the analysis context, never from AI arguments. Cross-project incidents/analyses/traces/tool results are covered by explicit integration tests (engineer on project A → 404 on everything in project B).

## How is prompt injection handled?

Layered prompt architecture with strict order of authority (system rules → application rules → evidence as DATA → user data as DATA). Evidence is wrapped and labeled as untrusted data; the model is instructed that instructions may only originate from layers 1–2. Tool results follow the same rule (rendered as DATA, pre-scanned). Defense in depth: a deterministic pre-scan strips obvious instruction-like sequences, and the **grounding rule** makes injection attempts visible — a factor citing a fake evidence ID fails validation. Tests plant "ignore previous instructions" text in runbooks/sources and assert it grants no capabilities.

## How is grounding enforced?

Mechanically, not by an LLM judge: every evidence citation must be an ID that exists in the evidence index, and every root-cause candidate must cite at least one. Empty evidence lists and unknown IDs are rejected; bounded repair (max 2) then safe failure (`422 AI_VALIDATION_FAILED`) — unvalidated prose is never returned as a primary result ([ADR-0007](adr/0007-structured-output-schema-validation.md)).

## How is RAG evaluated?

A deterministic runner (`python -m app.evaluation.run`) over the versioned 20-case golden dataset (`v1`): per-leg Recall@K / Precision@K / MRR / Hit Rate with document-level dedup, plus schema validity, mechanical grounding validity, evidence coverage, and per-case tool-loop metrics. Reports are JSON + Markdown, baseline comparison is supported, and everything runs on **mock providers** — zero Gemini, no API key. Numbers are labeled as synthetic-corpus/mock-embedding results; hybrid is not claimed superior to vector unless the data says so (it currently doesn't).

## Why no LLM-as-judge?

Cost, bias, and reproducibility: an LLM grading an LLM is expensive, unstable across runs/models, and undermines the evaluation's credibility. The first evaluation version is deterministic and mechanical (metrics + validators) so regressions are attributable. An LLM judge is a possible future layer, clearly labeled experimental.

## How are Gemini costs controlled?

- Normal tests, CI, health, readiness, and the evaluation runner make **zero** Gemini calls (all mocked).
- Live calls are explicitly gated (`RUN_GEMINI_TESTS=true`) and bounded (one smoke call per test).
- Concurrency is capped (`Analysis:MaxConcurrency`), tool calls bounded, per-job timeouts enforced.
- **429 handling**: bounded retries (default 3) with exponential backoff + jitter, respecting provider retry info; rate-limited submissions at the API (in-memory limiter) — no aggressive retry loops.

## How would you scale this?

Identified seams, not hypothetical rewrites (future-roadmap.md): the in-process queue → distributed queue (SQS/RabbitMQ) by swapping worker plumbing; the in-memory rate limiter → shared store; multi-instance workers need the queue + shared rate limiting + idempotency keys (already present via request ids); retrieval is already stateless against PostgreSQL and would benefit from read replicas; the dependency graph is rebuilt per analysis (a persisted graph is a documented next step); evaluation and tracing are already first-class so scaling decisions stay measurable.

## How would you deploy on AWS?

Documented, not built (deployment-strategy.md §4): CloudFront + S3 for the SPA → ALB → ECS Fargate for backend + AI service → RDS/Aurora PostgreSQL (+pgvector); Secrets Manager for keys; Cognito as the auth seam ([ADR-0012](adr/0012-auth-model.md)); CloudWatch for logs/metrics. Explicitly deferred until the local MVP is stable and a cost estimate is reviewed — nothing in the MVP requires AWS.

## What happens with multiple workers?

Each worker is stateless except the in-process queue; the source of truth is `analysis_runs`. With N instances, each hosts its own worker; a distributed queue is required to avoid duplicate processing (request-id idempotency already exists at the API, and `RecoverOnStartup` handles interrupted runs). Documented as single-instance in MVP; multi-instance is a Phase 10/AWS item, not an assumption.

## What are the biggest current limitations?

1. **Live Gemini structured output**: the configured `gemini-3.1-flash-lite` rejects the project's response-schema with HTTP 400 (provider compatibility issue; the abstraction keeps the model replaceable — never claim live analysis works).
2. Evaluation uses synthetic data + mock embeddings; numbers prove the framework, not production accuracy.
3. In-process queue + rate limiter are single-instance; no hosted deployment (local Docker is the MVP).
4. Dependency leg scores 0 on retrieval-style golden queries (it is change-model-driven) — reported honestly, not hidden.
5. No reranker, no LLM-judge, no GitHub/webhook integration yet.
