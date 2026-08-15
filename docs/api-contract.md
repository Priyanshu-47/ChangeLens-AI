# API Contract (Public Boundary)

> Phase 0 deliverable (updated Phase 5). Conventions, endpoint catalog, and key DTO shapes for the REST API the React SPA consumes. The AI service's internal contract lives in [ai-service-boundary.md](ai-service-boundary.md). Endpoints marked **Phase 1** are implemented; the rest are planned. The authoritative DTOs are generated from the ASP.NET Core OpenAPI document (`/swagger/v1/swagger.json`) — this document defines the contract they must satisfy.

## 1. Conventions

- **Base path:** `/api/v1` (version in the path; breaking changes bump the minor after deprecation, per SemVer-style policy).
- **Auth:** `Authorization: Bearer <JWT>` for all endpoints except `auth/*` and `health`. Service-to-service calls to the AI service are separate (internal key, not user JWT).
- **Errors:** uniform error envelope `{ "code": string, "message": string, "traceId": string, "details": object|null }` with conventional HTTP statuses (400 validation, 401, 403, 404, 409 conflict, 422 AI-validation, 429 rate-limited, 500). No stack traces leak.
- **Pagination:** `?page=1&pageSize=20` with `X-Total-Count` header and a stable list envelope `{ "items": [], "page": 1, "pageSize": 20, "total": 0 }`.
- **Ids:** `Guid` in the wire format (`"3fa85f64-…"`).
- **Timestamps:** ISO-8601 UTC.
- **Async pattern:** operations that take seconds (analyze, investigate, ingest, evaluation) return **`202 Accepted`** with a `Location` header pointing at the job resource. Clients poll; the job exposes `status` and, on success, a result reference. See §5. Rationale: [ADR-0009](adr/0009-async-analysis-jobs.md).

## 2. Endpoint catalog (MVP)

### Auth
| Method | Path | Notes |
| --- | --- | --- |
| POST | `/api/v1/auth/register` | Creates user (Engineer role default) |
| POST | `/api/v1/auth/login` | Returns `{ accessToken, expiresIn, user }` |
| GET | `/api/v1/auth/me` | Current user + memberships |

### Projects & code model
| Method | Path | Notes |
| --- | --- | --- |
| POST/GET | `/api/v1/projects` | Create / list (authz: member) — **Phase 1** |
| GET/PATCH | `/api/v1/projects/{projectId}` | Detail / update (Owner/Admin) — **Phase 1** (DELETE deferred) |
| POST/DELETE | `/api/v1/projects/{projectId}/members` | Manage members (Owner/Admin) — **Phase 1** |
| POST/GET | `/api/v1/projects/{projectId}/repositories` | Register / list repositories — **Phase 1** |
| GET | `/api/v1/repositories/{repositoryId}` | Repository detail — **Phase 1** |
| POST | `/api/v1/repositories/{repositoryId}/ingest` | 202 — parses, indexes, chunks, embeds (Phase 3) |
| GET | `/api/v1/projects/{projectId}/dependency-graph` | Nodes + edges for the interactive graph (Phase 4) |
| POST/GET | `/api/v1/projects/{projectId}/services` | Create / list services — **Phase 1** |
| GET | `/api/v1/services/{serviceId}` | Service detail — **Phase 1** |
| GET | `/api/v1/audit-logs?projectId=` | Project audit trail (Owner/Admin) — **Phase 1** |

### Changes & risk analysis (Workflow A)
| Method | Path | Notes |
| --- | --- | --- |
| POST | `/api/v1/pull-requests` | Submit a change (metadata + changed file contents or diff) |
| GET | `/api/v1/pull-requests/{prId}` | Change detail incl. parsed changed files |
| POST | `/api/v1/pull-requests/{prId}/analyze` | 202 → `analysisRunId`; runs Workflow A |
| GET | `/api/v1/analyses/{analysisRunId}` | Job status; result ref when complete |
| GET | `/api/v1/analyses?projectId=&type=` | List; used by dashboard + trace views |

### Incidents & investigation (Workflow B)
| Method | Path | Notes |
| --- | --- | --- |
| POST/GET | `/api/v1/incidents` | Create (with optional initial events) / list — **Phase 1** |
| GET/PATCH | `/api/v1/incidents/{incidentId}` | Detail incl. timeline events / update — **Phase 1** |
| POST | `/api/v1/incidents/{incidentId}/events` | Append a timeline event (log/error/deployment) — **Phase 1** |
| POST | `/api/v1/incidents/{incidentId}/investigate` | 202 → runs Workflow B async — **Phase 5** |
| GET | `/api/v1/analyses/{analysisRunId}` | Job status; validated result when complete — **Phase 5** (async polling) |
| GET | `/api/v1/analyses/{analysisRunId}/trace` | Per-stage observability trace + retrieval explorer — **Phase 7** (authz identical to the analysis) |
| GET | `/api/v1/incidents/{incidentId}/investigation` | Latest investigation result (Phase 6) |

### Evaluation
Phase 7 evaluation is a local CLI (`python -m app.evaluation.run`, docs/evaluation.md) producing JSON/Markdown reports under gitignored `data/evaluation-output/`; it forces mock providers (zero Gemini). The REST evaluation endpoints below remain **deferred** (a future hosted-run surface).

### Ops
| Method | Path | Notes |
| --- | --- | --- |
| GET | `/health` | Liveness (no dependencies) — **Phase 1** |
| GET | `/api/v1/health` | Dependency status (DB now; AI service added in Phase 2) — **Phase 1** |

## 3. Key DTO shapes

### Risk report (Workflow A result — validated, never prose)

```json
{
  "riskLevel": "HIGH",
  "confidence": 0.82,
  "impactedComponents": [
    { "componentId": "…", "name": "AuthClient", "service": "auth-api", "filePath": "src/clients/AuthClient.cs", "impact": "MODIFIED" }
  ],
  "riskFactors": [
    {
      "id": "…",
      "title": "Authentication client changed",
      "description": "AuthClient.cs changes the token refresh path; 3 components depend on it.",
      "severity": "HIGH",
      "evidence": [
        { "type": "ChangedFile", "reference": "src/clients/AuthClient.cs#L42-L58" },
        { "type": "HistoricalIncident", "reference": "INC-182" }
      ]
    }
  ],
  "historicalIncidents": [
    { "incidentId": "…", "reference": "INC-182", "similarity": 0.71, "summary": "Token refresh regression after AuthClient change", "evidence": "retrieved doc id …" }
  ],
  "recommendedTests": [
    { "category": "Regression", "targetComponent": "AuthClient", "description": "Refresh-token expiry with rotated signing key" }
  ],
  "unknowns": [ "No test coverage detected for TokenStore.RefreshAsync" ],
  "evidence": [ { "id": "…", "type": "ChangedFile", "reference": "src/clients/AuthClient.cs#L42-L58", "summary": "Diff hunk L42-58 modifies refresh logic", "aiDocumentId": "…" } ]
}
```

### Incident investigation (Workflow B result — Phase 5)

```json
{
  "rootCauseCandidates": [
    {
      "id": "cand-1",
      "title": "Signing-key rotation invalidated issued tokens",
      "confidence": 0.74,
      "status": "Candidate",
      "evidenceIds": [ "chunk:…" ],
      "reasoning": "The timeline places the deployment before the first 401.",
      "unknowns": [ "Whether the signer pod recycled before/after deploy" ]
    }
  ],
  "remediation": {
    "immediateMitigation": "Validate the new key against the token issuer.",
    "investigationSteps": [ "Correlate the first 401 with the rotation window." ],
    "recommendedRemediation": null,
    "validationSteps": [],
    "rollbackConsideration": "Evaluate rolling the rotation back.",
    "insufficientEvidence": false
  },
  "unknowns": [ "No database telemetry was available." ],
  "evidence": [ { "id": "chunk:…", "type": "Document", "source": "chunk:…", "summary": "…", "metadata": {} } ]
}
```

Every `rootCauseCandidates[].evidenceIds` entry MUST be an evidence id that exists in the
prompt's evidence index (enforced deterministically by the AI service grounding rule,
[ADR-0007](adr/0007-structured-output-schema-validation.md)); an empty list is rejected by schema validation.

### Analysis run (job resource — Phase 5)

```json
{
  "id": "…",
  "projectId": "…",
  "type": "IncidentInvestigation",
  "status": "Queued",
  "incidentId": "…",
  "result": { "rootCauseCandidates": [], "remediation": {}, "unknowns": [], "evidence": [] },
  "resultSchemaVersion": "incident-v1",
  "model": "mock-gemini-3.1-flash-lite",
  "promptVersion": "incident-v1",
  "queuedAtUtc": "…",
  "startedAtUtc": null,
  "completedAtUtc": null,
  "error": null
}
```

`status` is `Queued | Running | Succeeded | Failed`. The validated `result` is present only
when `Succeeded`; `error` is `{ "code": "LLM_RATE_LIMITED", "message": "…" }` when `Failed`
(never raw stack traces or secrets). `type` is `ChangeRisk` (synchronous Phase 2/4 slice)
or `IncidentInvestigation` (async Phase 5).

## 4. Authorization matrix (summary)

| Resource | Viewer | Engineer | Admin | Owner |
| --- | --- | --- | --- | --- |
| Read project data | ✅ (member) | ✅ | ✅ | ✅ |
| Submit changes / incidents / ingest | ❌ | ✅ | ✅ | ✅ |
| Run analyses / investigations / evaluations | ❌ | ✅ | ✅ | ✅ |
| Manage members / delete project | ❌ | ❌ | ✅ | ✅ |

Project membership is required for every project-scoped call ([ADR-0012](adr/0012-auth-model.md), [security-model.md](security-model.md)).

## 5. Async job semantics

1. `POST /api/v1/incidents/{incidentId}/investigate` returns `202 Accepted` with body `{ "analysisId", "status": "Queued", "statusUrl": "/api/v1/analyses/{analysisId}" }` and a `Location` header.
2. Job states: `Queued → Running → Succeeded | Failed`, enforced by a state machine (`AnalysisRun.TransitionTo`) — a job can never move backwards or be re-completed.
3. Failure codes (machine-readable `error.code`): `AI_VALIDATION_FAILED`, `LLM_RATE_LIMITED`, `AI_TIMEOUT`, `AI_UNAVAILABLE`, `JOB_TIMEOUT`, `QUEUE_FULL`, `WORKER_INTERRUPTED`, `INTERNAL`.
4. Idempotency: the body accepts a client-generated `requestId`; while a run with the same `projectId + requestId` is Queued/Running the submission returns the existing job (no duplicate AI spend). After a terminal state the same key starts a fresh run. The unique index only covers non-terminal statuses.
5. The queue is bounded and in-process (no Redis/Kafka); a full queue persists the run as `Failed(QUEUE_FULL)` rather than dropping it. Concurrency is capped (`Analysis:MaxConcurrency`, default 2). Transient AI failures (429/504/502) are retried with bounded backoff; 422 validation failures are never retried.
6. Frontend polls with backoff (1s, 2s, 4s… cap 10s); no websockets in MVP. A job can never stay `Running` forever — the per-job timeout fails it as `JOB_TIMEOUT`; interrupted jobs are recovered as `WORKER_INTERRUPTED` on startup.
