# Definition of Done — MVP

> The MVP is complete only when every item here is verifiably true. Each item is phrased as a testable statement. Items marked (P#) land in that phase but count toward MVP DoD.

## 1. Functional

- [ ] **Workflow A end-to-end:** a demo change submitted through the UI produces a stored `RiskReport` with `riskLevel`, `confidence`, `impactedComponents`, `riskFactors`, `historicalIncidents`, `recommendedTests`, `unknowns` — all schema-validated, with every risk factor referencing ≥1 evidence item. (P4, P5)
- [ ] **Workflow B end-to-end:** a demo incident produces a stored `IncidentInvestigation` with `severity`, `classification`, `rootCauseCandidates` (each with confidence, status, evidence), `recommendedInvestigationSteps`, `recommendedRemediation`, `unknowns` — validated, evidence-grounded. (P4, P5)
- [ ] **Evidence is inspectable:** the UI shows the evidence behind each conclusion and links to the underlying document/chunk/incident/deployment. (P5)
- [ ] **Hybrid retrieval is real:** retrieval uses vector + keyword + metadata filtering + RRF (never vector-only); results show per-source scores. (P3)
- [ ] **Project isolation:** a user of project A can never retrieve or view project B data, verified by tests. (P1, P3)
- [x] **Controlled tool use (Phase 8):** seven read-only, project-isolated tools are allowlisted in .NET; the AI proposes calls and the backend validates, authorizes, executes (bounded timeout), and audits every call; unknown tools → `TOOL_NOT_ALLOWED`, invalid args → `INVALID_ARGUMENT`, cross-project → `NOT_FOUND`, max calls → `TOOL_CALL_LIMIT_EXCEEDED`; calls are visible in the trace (`toolCalls`) and audit log (`ToolExecuted`/`ToolRejected`). No shell/SQL/URL/write tools; Python never executes tools. (P8)
- [x] **Evaluation is honest:** the Phase 7 runner produces real JSON/Markdown reports with per-leg Recall@K / Precision@K / MRR / Hit Rate over the versioned 20-case golden dataset, plus schema-validity, mechanical grounding, and evidence-coverage counts — using mock providers (zero Gemini). Hybrid is not claimed superior unless the measured numbers show it; metrics are labeled as synthetic-corpus/mock-embedding results. (P7)
- [x] **AI trace view:** any analysis shows model, prompt version, retrieval queries + retrieved documents, per-stage timings, and (Phase 8) tool calls with statuses/durations — via `GET /analyses/{id}/trace` and the React Trace panel + Retrieval Explorer + Tools-used list. Tokens/cost are recorded only when the provider exposes them. (P7, P8)

## 2. Quality & correctness

- [ ] **No uncontrolled prose** is ever returned as a primary analysis result (safe-failure path returns a structured error instead). (P2)
- [ ] **No fabricated data:** no metric, benchmark, incident, or result exists that wasn't produced by an actual run (unit tests, eval runs, or seeded demo data explicitly labeled as demo). (all)
- [ ] **Unknowns are honored:** schemas include `unknowns`; investigations distinguish Evidence / Hypothesis / Unknown / Recommendation in the UI. (P4, P5)
- [x] **Prompt-injection defense verified:** tests plant instruction-like content and assert it is not followed; tool results are rendered as untrusted DATA with the same pre-scan, and the tool layer remains authoritative (a runbook cannot enable a tool or change project scope). (P2/P8)
- [ ] **LLM is never used for deterministic work:** code review grep/test asserts parsing, dependency computation, and file-type checks are code, not LLM calls. (P4)
- [ ] **Model names are config:** no model id is hardcoded in source; `.env.example` is the contract; readiness probe validates availability. (P2)

## 3. Engineering

- [ ] `dotnet test` and `pytest` are green; integration tests use a real PostgreSQL (Testcontainers) and a mocked AI service. (P1, P2)
- [ ] Retrieval and schema-validation unit tests cover the hybrid pipeline and the repair loop. (P2, P3)
- [ ] `docker compose up` (or the documented equivalent) runs frontend + backend + ai-service + postgres on a clean machine and reaches a seeded, working demo. (P9)
- [ ] GitHub Actions CI: lint → unit → integration → security scan → AI evaluation (regression gate) → docker build, all green, with **zero Gemini spend in CI**. (P9)
- [ ] Swagger/OpenAPI for the backend is current; frontend client types are generated from it. (P1, P5)
- [ ] Audit log records auth events, mutations, and tool calls; audit data is append-only. (P1, P6)

## 4. Portfolio completeness

- [ ] Architecture + sequence diagrams, ER diagram, API documentation, ADRs (all 12), security model, deployment strategy, and evaluation methodology are in `docs/` and consistent with the code. (P0 + maintained)
- [ ] Demo dataset and golden dataset are versioned in `data/` with documented provenance. (P3, P7)
- [ ] Known limitations are documented (C# first-class parsing, single provider live, free-tier hosting caveats, scale ceilings). (P0 + maintained)
- [ ] Screenshots of all primary screens and the actual evaluation results accompany the README. (P5, P7)
- [ ] A reviewer can go from README → `docker compose up` → both workflows in under 15 minutes with no paid service. (P9)

## Sign-off

MVP is done when every box above is checked by an actual run — not by declaration. The final review pass re-runs: both workflows, one full evaluation, one prompt-injection test, and the clean-machine compose check.
