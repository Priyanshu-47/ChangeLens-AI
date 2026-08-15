# Future Roadmap

> Phase 10 deliverable. Categorized by intent, not commitment. **Nothing here is implemented** unless marked done; items are ordered by value and cost. The engineering project (Phases 0–10) is complete; these are the deliberately deferred ideas.

## CURRENT (this repository — complete)

- Change risk analysis (Roslyn + dependency graph → grounded risk report)
- Async incident investigation (202 + poll, job state machine, bounded queue)
- Hybrid RAG (vector + keyword + metadata + dependency → RRF), project-isolated
- Controlled tool loop (7 read-only tools; .NET validates/authorizes/executes/audits)
- Deterministic evaluation (20-case golden dataset, per-leg ablation, per-case tool trace)
- AI trace (stages, retrieval legs, tool calls, failure categories) + trace API + UI
- AuthN/AuthZ, audit log, project isolation, prompt-injection defense, grounding
- Production hardening: Docker Compose (4 services), controlled CORS, rate limiting, non-dev secret validation, $0 CI, portfolio docs

## NEXT (highest value per effort, in rough order)

1. **Resolve live Gemini structured-output compatibility** (the configured `gemini-3.1-flash-lite` rejects the current response-schema with HTTP 400). The provider abstraction already isolates this; a targeted schema/`response_schema` fix would turn the mock-verified pipeline into a live-verified one. Then re-verify with the existing gated smoke tests — **without burning quota on diagnostics**.
2. **Re-embed the demo corpus with real Gemini embeddings** (`gemini-embedding-2`, 768-dim) once live is verified, and re-run the evaluation to replace mock-embedding numbers with real ones (labeled as such).
3. **Docker clean-start verification on a Docker-equipped machine** (`docker compose up --build` + canonical demo + `down -v` clean start) — the one remaining Phase 9/10 exit criterion this environment couldn't run.
4. **Hosted evaluation dashboard**: expose evaluation runs through the API and render metrics/case drill-downs in the UI (the runner is currently a CLI).
5. **GitHub/webhook integration**: ingest real PR diffs instead of the seeded/local-git demo change source.

## FUTURE (needs measured justification or a real deployment)

- **Reranker** — only if the ablation shows RRF underperforms on real (non-synthetic) data; the evaluation framework exists precisely to make this decision data-driven.
- **Persistent dependency graph** — today the Roslyn graph is rebuilt per analysis; persisting it would speed repeated analyses and enable drift detection (Neo4j only if scale demands; SQL/jsonb first).
- **Human approval for higher-risk tools** — the risk-level policy hook exists (`ToolRiskLevel`); a MEDIUM/HIGH tool would require approval before execution.
- **Distributed queue + multi-instance workers** — replace the in-process `Channel` with SQS/RabbitMQ once a real deployment needs more than one instance; `AnalysisRun` + request-id idempotency already provide the seam.
- **LLM-as-judge** — explicitly experimental and deferred (cost, bias, reproducibility); deterministic evaluation comes first.
- **Hosted deployment** — local Docker remains the official MVP; a free/low-cost host (documented options in deployment-strategy.md §3) or AWS (CloudFront/S3 → Fargate → RDS, Secrets Manager, Cognito seam, CloudWatch) only when the local MVP is stable and cost estimates are reviewed.

## Explicitly NOT planned

Multi-agent architectures, arbitrary web browsing, SQL/shell/write tools, Redis/Kafka/Kubernetes/Neo4j/Elasticsearch/LangSmith/Langfuse/Datadog, or LLM-judge as the primary evaluation mechanism. Each was considered and rejected for cost, complexity, or lack of measured need — the decisions are recorded in the ADRs.
