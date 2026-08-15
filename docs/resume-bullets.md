# Resume Bullets & Project Summaries

> Phase 10 deliverable. Every claim below is implemented, tested, and measured in this repository. Dataset numbers come from the actual evaluation run (`data/evaluation-output/evaluation-report.json`) and are labeled with their context — synthetic corpus, deterministic/mock providers. Never present them as production accuracy.

## Three resume bullets

1. **Built a full-stack AI platform** (React + ASP.NET Core 10 + FastAPI + PostgreSQL/pgvector) with two production workflows — grounded change-risk analysis and async incident investigation — including a controlled tool loop where the AI proposes calls and the backend validates, authorizes, executes, and audits them (no shell/SQL/write tools).

2. **Designed a hybrid retrieval + evaluation system**: structure-aware chunking (tree-sitter), pgvector + PostgreSQL FTS + dependency leg merged with RRF, hard project isolation in every query, and a deterministic 20-case golden-dataset runner (Recall@K / MRR / grounding / schema / tool-loop metrics) that runs at $0 on mock providers with zero Gemini calls.

3. **Shipped security and reliability as product**: JWT/RBAC with cross-project isolation (404 for invisible resources), prompt-injection defense via layered prompt architecture, mechanical grounding enforcement, per-analysis traces with real stage timings, async jobs with an enforced state machine, in-memory rate limiting, non-dev secret validation, and $0 GitHub Actions CI (backend/Python/React/evaluation/secret-scan).

## Compact version (2–3 lines for a resume summary)

> ChangeLens AI — full-stack AI platform for production change risk and incident investigation: Roslyn-based change intelligence with a dependency graph, hybrid RAG (pgvector + FTS + dependency leg, RRF fusion), schema-validated and evidence-grounded AI output, a controlled tool loop, deterministic golden-dataset evaluation, and per-analysis traces. React + ASP.NET Core 10 + FastAPI + PostgreSQL, $0-first, 187 .NET unit + 53 integration + 157 Python + 12 DB + 34 React tests green.

## Detailed project version (for a project section or interview packet)

**Problem:** post-incident reviews are slow because evidence (git history, deployment logs, runbooks, past incidents, source) is scattered; pre-deploy risk review is usually a manual diff read.

**Solution:** treat it as an engineering problem — a change model (Roslyn symbol analysis + dependency graph) feeds hybrid retrieval over code/incidents/runbooks, an LLM produces schema-validated, evidence-cited analysis, and a controlled tool loop lets the model gather more evidence under application authorization. Everything is traceable: stages, retrieval legs, tool calls, timings.

**Two workflows:**
- *Change Risk:* code change → Roslyn → dependency graph → hybrid retrieval → grounded risk report (risk level, confidence, impacted components, risk factors, evidence).
- *Incident Investigation:* incident → `202` async job → normalized context → hybrid retrieval → tool loop → grounded root-cause candidates + remediation + explicit unknowns → pollable result + trace.

**Key engineering decisions:**
- AI proposes tools; **.NET** validates/authorizes/executes/audits (7 read-only, project-isolated tools; bounded loop; adversarial tests).
- Hybrid RAG with per-leg attribution and RRF; project isolation enforced twice (handler + SQL).
- Deterministic evaluation: 20-case golden dataset, per-leg Recall@K/MRR/Hit Rate, mechanical grounding, per-case tool trace — honest numbers (hybrid does **not** beat vector on the synthetic corpus; the ablation documents that).
- $0-first: local Docker + PostgreSQL + mock providers; CI has zero Gemini spend.

**Measured (synthetic AcmePay v1 dataset, mock providers):** 20/20 cases evaluated; schema-valid 20/20; grounded 20/20; tool loop 20/20 completed, 40/40 proposals valid; vector Recall@5 0.625 / MRR 0.975; keyword Recall@5 0.529 / MRR 1.000; hybrid Recall@5 0.583 / MRR 1.000; dependency leg 0 on retrieval-style queries (change-model-driven — reported, not hidden).

**Tests:** 187 .NET unit · 53 .NET integration (real PostgreSQL) · 157 Python unit · 12 Python DB integration · 34 React · production build green — all CI at $0.

**Known limitation (documented, not hidden):** the configured live Gemini model currently rejects the project's structured-output schema with HTTP 400; the provider abstraction keeps the model replaceable, and all tests/eval run on deterministic mocks.
