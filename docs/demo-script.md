# Demo Script (3–5 minutes)

> Phase 9 deliverable. The canonical ChangeLens demo: **JWT signing-key rotation → HTTP 401 incident → async investigation → controlled tool loop → grounded root causes**. Everything runs locally with mock AI providers — deterministic, free, no API key. **Do not claim live Gemini analysis**: the configured `gemini-3.1-flash-lite` structured-output schema currently returns HTTP 400 (a known, documented provider-compatibility issue). The provider abstraction means the same code path runs either.

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

Open `http://localhost:8080` and log in with the seeded demo user (`engineer` / documented demo password).

## 0:00 — Problem (30s)

> "ChangeLens answers two questions engineers ask before and after every change: *what is this change likely to break?* and *what actually broke — and what evidence supports the root cause?* Every answer is traceable to evidence, and the system is explicit about what it does **not** know."

## 0:30 — Dashboard (30s)

Show the project dashboard: real counts of incidents/analyses/services, the demo project context in the top bar, and the two core workflows in the sidebar (Incidents, Analyses, Change Risk).

## 1:00 — Incident (20s)

Open the AcmePay incident list, search **401**, open the JWT authentication-failure incident. Point out: severity, service, the chronological timeline (deployment detected → HTTP 401 spike → incident created), symptoms, known facts.

## 1:20 — Investigate (10s)

Click **Investigate Incident** → `202 Accepted` → navigates to the analysis page showing **Queued → Running**. Explain the async architecture: bounded in-process queue + background worker (no Redis/Kafka; $0-first).

## 1:45 — Root causes (20s)

Result page: **Root Cause Candidates** with confidence bars, evidence counts, and the grounding badge (`VALID`). Expand a candidate and click an evidence ID — it scrolls to the evidence card (historical incident, runbook, `TokenService.cs` source). Emphasize **evidence vs. AI inference** are visually distinct.

## 2:20 — Tool trace (25s)

Open **Analysis Trace** → **Tool Calls**: the AI proposed `get_dependency_paths(TokenService)` and `get_runbook(authentication-failure)`; .NET validated, authorized, executed, and audited each call (statuses + durations). Explain the security boundary: *the AI proposes, the application authorizes and executes* — no SQL, shell, or write tools exist.

## 2:40 — Dependency evidence (20s)

In the Retrieval Explorer, show the vector/keyword/dependency leg badges per chunk (with the "not directly comparable" note) and the dependency path `TokenService → IssueServiceToken → Program` from the change model.

## 3:00 — Change Risk (30s)

Open **Change Risk**, submit the JWT signing-key rotation change (the intentionally uncommitted `TokenService.cs` demo scenario). Show: risk level, confidence, changed/impacted symbols, dependency paths, risk factors, evidence.

## 3:30 — Evaluation (30s)

Run the deterministic evaluation:

```bash
cd ai-service
DATABASE_URL="postgresql+psycopg://changelens:changelens_dev_password@localhost:5432/changelens" \
./.venv/Scripts/python -m app.evaluation.run
```

Show `data/evaluation-output/evaluation-report.md`: 20/20 cases, per-leg Recall@K/MRR/Hit Rate (vector vs keyword vs dependency vs hybrid), grounding 20/20, schema validity, per-case tool trace (proposed/executed/rejected, loop completion, grounding after tools). **Be honest**: hybrid does not beat vector alone on this synthetic dataset — the ablation is the point.

## 4:00 — Architecture explanation (1 min)

Whiteboard/mermaid the four layers: React → ASP.NET Core 10 (authz, domain, orchestration, audit) → FastAPI (prompts, structured output, grounding) → PostgreSQL + pgvector (hybrid RAG). Roslyn + dependency graph in .NET; evaluation + trace first-class. Cost: **$0** (docs/costs.md). Gemini is behind a provider abstraction; the live structured-output schema issue is documented, not hidden.

## Cleanup

`docker compose down` (keeps data) or `docker compose down -v` (fresh start).
