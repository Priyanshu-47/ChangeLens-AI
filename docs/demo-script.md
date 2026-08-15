# Demo Script (3–5 minutes)

> Phase 10 deliverable. The canonical ChangeLens demo: **JWT signing-key rotation → HTTP 401 incident → async investigation → controlled tool loop → grounded root causes**. Everything runs locally with mock AI providers — deterministic, free, no API key. **Do not claim live Gemini analysis**: the configured `gemini-3.1-flash-lite` structured-output schema currently returns HTTP 400 (a known, documented provider-compatibility issue). The provider abstraction means the same code path runs either way.

## Before the demo

```bash
# one-time
cp .env.example .env                 # fill INTERNAL_API_KEY / JWT_SIGNING_KEY
docker compose up -d --build
# seed the demo corpus (mock embeddings, idempotent)
cd ai-service
DATABASE_URL="postgresql+psycopg://changelens:changelens_dev_password@localhost:5432/changelens" \
EMBEDDING_PROVIDER=mock ./.venv/Scripts/python scripts/seed_demo.py
```

Open `http://localhost:8080` and log in with `engineer@changelens.dev` / `EngineerPass!2026` (development-only seeded account).

## 0:00 — Problem (30s)

> "ChangeLens answers two questions engineers ask before and after every change: *what is this change likely to break?* and *what actually broke — and what evidence supports the root cause?* Every answer is traceable to evidence, and the system is explicit about what it does **not** know. It is a full-stack engineering platform, not a chatbot: the LLM reasons over structured, validated contracts, while deterministic systems do the exact work."

## 0:30 — Architecture (30s)

Draw the four layers: **React** → **ASP.NET Core 10** (authz, domain, orchestration, audit, Roslyn, tool execution) → **FastAPI** (providers, prompts, structured output, hybrid retrieval) → **PostgreSQL + pgvector** (app schema + ai schema). Two workflows — Change Risk (Roslyn + dependency graph + grounded report) and Incident Investigation (async job + controlled tool loop). Cost: **$0** ([docs/costs.md](costs.md)); Gemini behind a provider abstraction.

## 1:00 — Incident (20s)

Open the AcmePay incident list, search **401**, open the JWT authentication-failure incident. Point out: severity, service, the chronological timeline (deployment detected → HTTP 401 spike → incident created), symptoms, known facts.

## 1:20 — Investigate (10s)

Click **Investigate Incident** → `202 Accepted` → navigates to the analysis page showing **Queued → Running**. Explain the async architecture: bounded in-process queue + background worker, enforced job state machine, no Redis/Kafka — $0-first ([ADR-0009](adr/0009-async-analysis-jobs.md)).

## 1:50 — Root causes (20s)

Result page: **Root Cause Candidates** with confidence bars, evidence counts, and the grounding badge (`VALID`). Expand a candidate and click an evidence ID — it scrolls to the evidence card (historical incident, runbook, `TokenService.cs` source). Emphasize **evidence vs. AI inference** are visually distinct, and the "model confidence reflects assessment of supplied evidence" disclaimer.

## 2:10 — Evidence (20s)

Open the Evidence Explorer: types (Historical Incident / Runbook / Source Code / Dependency) with badges, and the grounding contract — every cited evidence ID exists; empty/unknown ids are rejected.

## 2:30 — Tool trace (20s)

Open **Analysis Trace** → **Tool Calls**: the AI proposed `get_dependency_paths(TokenService)` and `get_runbook(authentication-failure)`; .NET validated, authorized, executed, and audited each call (statuses + real durations). Explain the security boundary: *the AI proposes, the application authorizes and executes* — no SQL, shell, or write tools exist; max calls bounded.

## 2:50 — Dependency intelligence (20s)

In the Retrieval Explorer, show the vector/keyword/dependency leg badges per chunk (with the "not directly comparable" note) and the dependency path `TokenService → IssueServiceToken → Program` from the Roslyn change model. This is retrieval that knows the codebase structure.

## 3:10 — Change Risk (20s)

Open **Change Risk**, submit the JWT signing-key rotation change (the intentionally uncommitted `TokenService.cs` demo scenario). Show: risk level (MEDIUM), confidence, changed/impacted symbols, dependency paths, risk factors, evidence, validation status.

## 3:30 — Evaluation (30s)

Run the deterministic evaluation:

```bash
cd ai-service
DATABASE_URL="postgresql+psycopg://changelens:changelens_dev_password@localhost:5432/changelens" \
./.venv/Scripts/python -m app.evaluation.run
```

Show `data/evaluation-output/evaluation-report.md`: 20/20 cases, per-leg Recall@K/MRR/Hit Rate (vector vs keyword vs dependency vs hybrid), grounding 20/20, schema validity 20/20, per-case tool trace (proposed/executed/rejected, loop completion, grounding after tools). **Be honest**: hybrid does not beat vector alone on this synthetic/mock dataset — the ablation is the point; the dependency leg's 0 is change-model-driven, reported, not hidden.

## 4:00 — Security (30s)

Project isolation (cross-project → 404, integration-tested), prompt-injection defense (evidence is data, never instructions), secrets (env-only, non-dev fail-fast, no logs), audit trail (every tool call), rate limiting, controlled CORS.

## 4:30 — Architecture decisions (20s)

Point at the ADRs: why .NET + Python (ADR-0002), why hybrid RAG + RRF (ADR-0004), why async in-process jobs (ADR-0009), why single-agent controlled tools (ADR-0008), why evaluation is first-class (ADR-0010). Full Q&A in [docs/interview-prep.md](interview-prep.md).

## 4:50 — Limitations (10s)

Live Gemini structured output returns HTTP 400 (documented, provider-compatibility; the abstraction keeps the model replaceable); evaluation numbers are synthetic/mock; in-process queue + rate limiter are single-instance; no hosted deployment yet. Honest limitations are a feature.

## Cleanup

`docker compose down` (keeps data) or `docker compose down -v` (fresh start).
