# MVP Scope

> What the MVP must and must not be. Anything not listed here is out of scope until explicitly added to a later phase.

## MVP goal

A locally-runnable product that demonstrates, end to end, **both core workflows on demo data** with evidence-grounded, schema-validated AI results, hybrid retrieval, a polished UI, real evaluation numbers, and full AI-run observability — at **$0 infrastructure cost**.

## MVP user stories

| # | Story | Phase |
| --- | --- | --- |
| S1 | As an engineer, I submit a pull request / change, and I receive a structured risk report (risk level, confidence, impacted components, risk factors, historical incidents, recommended tests) where every conclusion links to inspectable evidence. | 4 |
| S2 | As an engineer, I open an incident and I receive an investigation with root-cause candidates, per-candidate evidence, confidence, unknowns, and recommended next steps — with evidence/hypothesis/unknown clearly distinguished. | 4 |
| S3 | As a user, I can see an interactive dependency graph of the components affected by a change. | 5 |
| S4 | As a platform owner, I can run evaluation over the golden dataset and see *measured* retrieval + pipeline metrics (Recall@K, precision, MRR, groundedness, latency, tokens, estimated cost), never fabricated. | 7 |
| S5 | As a platform owner, I can inspect the AI run trace for any analysis (model, prompt version, retrieval queries, retrieved docs, tool calls, tokens, cost, validation status). | 8 |
| S6 | As an engineer, I can ingest a repository (or use the seeded demo repository), incidents, and runbooks; ingestion respects document types and metadata. | 3 |

## In scope (by phase)

| Area | Included |
| --- | --- |
| **Backend domain** | Projects, repositories, services, components, dependencies, changes/pull requests, incidents + events + resolutions, deployments, risk reports, analyses, evaluations, audit logs. |
| **Ingestion** | Source code (C# first-class via Roslyn + tree-sitter chunking; JS/TS, Python, JSON, YAML best-effort), OpenAPI definitions, structured incident records, Markdown runbooks, deployment records. |
| **RAG** | Semantic chunking per document type, embedding provider abstraction (Gemini + local), hybrid retrieval (vector + keyword + metadata + RRF), project-scoped filtering, optional local reranker. |
| **AI reasoning** | Workflow A risk analysis and Workflow B incident investigation as **schema-validated structured outputs** with bounded repair; controlled tool use (Phase 6) with audit; no uncontrolled prose for primary results. |
| **Frontend** | Dashboard, change analysis view, incident investigation view, dependency graph, evaluation dashboard, AI run trace, evidence inspection. |
| **Evaluation** | Golden dataset (small, ~15–25 cases), comparison of keyword-only vs vector-only vs hybrid retrieval vs full pipeline; measured metrics stored in DB and shown in UI. |
| **Security** | JWT + Identity, RBAC roles, project-level authorization, prompt-injection defense, input/file validation, payload limits, rate limiting, audit log, env-var secrets. |
| **Ops** | Docker compose (frontend, backend, ai-service, postgres), health endpoints, structured logging, CI with lint + unit + integration + eval regression gate. |

## Explicitly out of scope for MVP

- **Real SCM integration** (GitHub App webhooks, git diff fetching). Changes are submitted via the API and/or seeded demo data. Roslyn parses the actual changed files regardless of how they arrive.
- **Multiple LLM providers live.** The abstraction exists (ADR-0005); only Gemini is implemented in MVP.
- **Multi-tenancy / organizations.** Projects provide data isolation; the concept is project-scoped authorization, not a SaaS tenancy model.
- **SSO / external identity providers.** Local Identity accounts + JWT only; Cognito/Entra is a Phase 10 note.
- **Async message queue.** No Redis/Kafka/SQS in MVP (ADR-0003, §8 of architecture).
- **Real-time / websockets.** Polling-based job status is sufficient.
- **Advanced observability stack** (OpenTelemetry exporters, tracing backends). MVP records AI-run metadata in the DB and exposes a trace view; structured logs to stdout.
- **Infrastructure as code for AWS.** Terraform/modules are Phase 10.
- **OCR / image inputs.** Not in the document types for MVP.

## Scope guardrails (hard rules)

- No feature is "placeholder presented as complete" — anything shipped must be exercised by a test, demo data, or an evaluation run.
- No AI conclusion is presented without evidence references; no metric is displayed unless an evaluation run produced it.
- No paid infrastructure anywhere in the MVP path.
- No new technology without a written justification in this repo (architecture doc or ADR).
