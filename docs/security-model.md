# Security Model

> Phase 0 deliverable (updated Phase 9). Threat focus, authentication, authorization, prompt-injection defense, secrets, validation, audit, and a production checklist. **Phase 1 implements:** Identity + JWT, RBAC + project-level authorization (404 for invisible projects, 403 for insufficient roles, global-Admin bypass), DataAnnotations + service validation, payload limits, uniform ProblemDetails errors, and an append-only audit trail (auth events + mutations). **Phase 5 adds:** async analysis authorization (submit = Write, poll = Read), cross-project isolation for analyses, safe job failure states, and analysis lifecycle audit events. **Phase 9 adds:** controlled CORS (no wildcards), in-memory rate limiting on analysis submission, non-dev fail-fast validation of JWT / AI key / DB connection string, and the production checklist in §8. Deferred to later phases: mTLS between services (11), external IdP (11).

## 1. Threat focus (what this system must defend)

| Threat | Why it matters here | Primary control |
| --- | --- | --- |
| **Prompt injection via ingested content** | Repo files, READMEs, logs, incidents are untrusted user data that flows into LLM prompts | Layered prompt architecture (system > app rules > evidence > user data) — §4 below |
| **Cross-project data access** | Project isolation is the product's data boundary | Server-enforced project filter + authz policy, enforced twice (SQL + handler) |
| **Secret leakage** | API keys, JWT signing keys | Env-only config, `.env.example` contract, git-ignore, no secrets in code or logs |
| **Unauthorized AI spend** | Free-tier LLM budget is a real resource | AuthN on all analysis endpoints, rate limiting, eval cost guard |
| **Tool misuse (Phase 6)** | Tools read code/incidents/deployments | Tool schemas + per-call authorization + audit in .NET; AI service only proposes |
| **Abuse of public endpoints** | Registration, ingestion payloads | Validation, payload limits, rate limiting, file validation |
| **Supply chain** | Python/C#/npm dependencies | Lockfiles, CI dependency scan (Phase 10) |
| **Prompt-injection exfiltration of secrets** | Model could be told to echo env contents | Model never receives secrets; system prompt prohibits; deterministic pre-scan strips env-like tokens from evidence |

## 2. Authentication (MVP)

- **ASP.NET Core Identity** (local accounts) issuing **JWT bearer** tokens; HS256 signing key from env (`JWT__SIGNING_KEY`) for local dev; rotation supported, managed secrets (Secrets Manager) on AWS.
- Seed accounts: `admin` (Owner), `engineer`, `viewer` — used by demo dataset and integration tests.
- Passwords hashed with Identity's PBKDF2 (defaults); no plaintext anywhere.
- AI service is **not** in the user-auth path: it authenticates the backend via `INTERNAL_API_KEY` header + network isolation (compose-internal network; mTLS documented as the Phase 11 hardening).

## 3. Authorization

- **Roles:** Admin, Engineer, Viewer (`Owner` project role superset). Implemented as ASP.NET Core policies.
- **Project-level:** `project_members(project_id, user_id, role)`; a custom `IAuthorizationHandler` resolves the project id from the route/body and checks membership+role. Every project-scoped query additionally filters by `project_id` at the data layer (defense in depth — a handler bug cannot leak data).
- **Async analyses (Phase 5):** submitting an investigation requires Write (Engineer+); polling an analysis requires Read (Viewer may poll). Non-members get 404 for both the incident and the analysis — cross-project analysis ids are not inferable. The worker re-loads the incident by its id and the AI service hard-filters every retrieval query by `projectId`; a user can never submit an investigation for, or read the result of, another project's analysis (explicit integration test). A full bounded queue never drops a job silently — the run is persisted `Failed(QUEUE_FULL)` and the 202 still returns its id so polling surfaces the truth.
- **AI service:** enforces the `projectId` filter passed in (validates the backend is scoped); derives nothing itself ([ADR-0002](adr/0002-service-boundary.md)).
- **Tools (Phase 6):** each tool call is validated against its schema, authorized for the project, executed with timeout/retry limits, and audit-logged — including rejected calls ([ADR-0008](adr/0008-controlled-tool-use.md)).

## 4. Prompt-injection defense (layered, primary control = prompt architecture)

Order of authority in every prompt, strictest first ([llm-integration.md](llm-integration.md) §4):

```
1. System instructions          (static, trusted)
2. Application rules            (static, trusted — incl. grounding + data-vs-instruction rule)
3. Retrieved evidence           (untrusted — wrapped in <evidence> tags, labeled as DATA)
4. User-provided data           (untrusted — the change/incident, quoted and labeled as DATA)
```

Concrete rules enforced in the prompt + code:
- The model is instructed that **instructions may only originate from layers 1–2**; anything in layers 3–4 is data, regardless of its content ("ignore previous instructions" inside a file has no authority).
- Every evidence item carries an id and type; the grounding rule forces the model to reference evidence ids — which makes injection attempts visible in the result (a factor citing a fake id fails validation).
- Deterministic pre-scan strips/escapes obvious instruction-like sequences from evidence before it reaches the prompt (defense-in-depth, not the primary control).
- Model outputs never include raw prompt content (schema validation rejects it).

**Phase 8 tool results** follow the same rules as evidence: rendered inside `<tool_results>` as DATA, pre-scanned, and explicitly non-authoritative (the tool prompt forbids following instructions inside results). Tool outputs are sanitized structured JSON with executor-declared `evidenceIds`; only those ids are citable (grounding). The tool layer is authoritative — a runbook cannot enable a disabled tool, change authorization, or alter project scope.

### Tool authorization (Phase 8, [docs/agent-tools.md](agent-tools.md))

- **Allowlist only:** the `ToolRegistry` is explicit DI registration; unknown names are rejected (`TOOL_NOT_ALLOWED`) before any execution.
- **Project isolation:** the project id comes from the authenticated analysis context, never from AI arguments; cross-project lookups return `NOT_FOUND` (no existence leak).
- **Argument validation:** wrong types, invalid UUIDs, empty identifiers, out-of-range values, and path-like/URI/shell-metacharacter symbols are rejected (`INVALID_ARGUMENT`) before execution.
- **Bounded execution:** per-tool timeout (`Analysis:ToolTimeoutSeconds`), per-analysis call cap (`Analysis:MaxToolCalls` → `TOOL_CALL_LIMIT_EXCEEDED`).
- **No powerful tools:** no SQL, no shell, no arbitrary URL fetching, no write/deploy tools — all Phase 8 tools are LOW-risk and read-only.

## 5. Secrets management

- All secrets via environment variables; `.env.example` documents the full contract; `.env*` git-ignored (except the example).
- API keys never appear in: source code, prompts, logs (structured logger redacts key-like values), error responses, or AI-run metadata.
- Docker: secrets via compose `env_file` for dev; Docker Secrets / Secrets Manager documented for shared deployments.
- CI never receives production keys — only test-local keys for mocked integrations.

## 6. Input & payload validation

- **DTO validation:** DataAnnotations + endpoint filters (and Pydantic on the AI service) on every public/internal boundary; 400 with the standard error envelope.
- **Payload limits:** body size cap (backend middleware + AI service), per-document size cap (5 MB), batch caps, `pageSize` caps.
- **File validation (ingestion):** allowed extensions + MIME sniffing + content-size + hash dedupe; content is treated as data, never executed.
- **Rate limiting:** ASP.NET Core built-in rate limiter on auth + analysis + ingestion endpoints (429 handling documented); AI service rate-limits internal ingest/search calls per backend key.
- **CORS:** allow only the SPA origin(s) from config; no wildcards with credentials.
- **HTTPS/TLS:** enforced in any shared deployment; local dev HTTP only inside compose.

## 7. Audit logging

Append-only `audit_logs` records for: authentication events, all mutating operations, **every tool call (proposed/executed/rejected with reason)**, AI analysis runs (via `analysis_runs`), evaluation runs, member/role changes, and project deletions. Fields: `occurred_at, user_id, action, resource_type, resource_id, ip_address, details`. Audit data is not user-editable via any API.

Async analysis lifecycle (Phase 5) is audited as: `AnalysisRequested` (submit), `AnalysisStarted`, `AnalysisCompleted`, `AnalysisFailed` (worker). Details carry the analysis/incident ids, model, prompt version, validation status, latency, candidate/evidence counts (completed) or the safe failure code + message (failed) — never secrets, raw prompts, or stack traces.

## 8. Production checklist (Phase 9 hardening)

Before any shared deployment (not local dev):

- [ ] **Secrets**: `GEMINI_API_KEY`, `JWT__SIGNING_KEY`, `INTERNAL_API_KEY`, and the DB password come from a secrets manager / platform secrets — never `.env`, never the repo. The backend fails fast on the dev placeholders (`dev-only-…`, `change-me-…`) outside `Development`, and the AI service requires `GEMINI_API_KEY` when `AI_PROVIDER=gemini`.
- [ ] **JWT**: real random HS256 signing key (≥ 256 bits), explicit issuer/audience, sane expiry, clock skew ≤ 1 min. No `dev-only` key anywhere in non-dev config.
- [ ] **CORS**: `Cors__AllowedOrigins` set to the exact SPA origin(s) — never `*`. The compose nginx proxy makes the production SPA same-origin, so CORS is usually empty.
- [ ] **AI boundary**: the FastAPI service is not publicly reachable (remove the dev `ports:` mapping in compose); only the backend calls it, over the internal network with `INTERNAL_API_KEY` + contract version header. mTLS remains a Phase 10+ hardening option.
- [ ] **Rate limiting**: analysis submission is limited in-memory (permit/window configurable via `RateLimit__*`). Documented as single-instance — a multi-instance deployment must move to a shared store.
- [ ] **HTTPS/TLS**: enforced at the edge (reverse proxy / platform TLS); local dev HTTP is fine inside compose.
- [ ] **Database**: non-default password, least-privilege role, network-restricted, TLS where supported.
- [ ] **Logging**: structured JSON with correlation/analysis/tool ids; no secrets, JWT, or authorization headers logged (redaction is built into the structured logger).
- [ ] **Health/readiness**: never call Gemini (probe off by default); `/api/v1/health` includes the DB check and gates compose startup.
- [ ] **Secrets scan**: the CI `secret-scan` job greps for credential-shaped material; run it before any push.

## 9. What is deliberately deferred (documented, not forgotten)

- mTLS between backend and AI service (Phase 11 AWS hardening; compose network + shared key in MVP).
- External IdP / SSO (Cognito/Entra) — Phase 10 note.
- Secrets rotation automation, full OWASP ASVS pass, dependency scanning in CI (Phase 10), WAF (Phase 11).
