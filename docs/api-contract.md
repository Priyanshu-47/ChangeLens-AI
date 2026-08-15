# API Contract (Public Boundary)

> Phase 0 deliverable (updated Phase 1). Conventions, endpoint catalog, and key DTO shapes for the REST API the React SPA consumes. The AI service's internal contract lives in [ai-service-boundary.md](ai-service-boundary.md). Endpoints marked **Phase 1** are implemented; the rest are planned. The authoritative DTOs are generated from the ASP.NET Core OpenAPI document (`/swagger/v1/swagger.json`) — this document defines the contract they must satisfy.

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
| POST | `/api/v1/incidents/{incidentId}/investigate` | 202 → runs Workflow B |
| GET | `/api/v1/incidents/{incidentId}/investigation` | Latest investigation result |

### Evaluation (Phase 7)
| Method | Path | Notes |
| --- | --- | --- |
| POST | `/api/v1/evaluations/run` | 202 — runs golden dataset eval (config: strategies to compare) |
| GET | `/api/v1/evaluations` | List runs |
| GET | `/api/v1/evaluations/{id}` | Stored metrics for the dashboard |

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

### Incident investigation (Workflow B result)

```json
{
  "severity": "SEV2",
  "classification": "DeploymentRegression",
  "rootCauseCandidates": [
    {
      "id": "…",
      "title": "Cached token signing key mismatch",
      "confidence": 0.74,
      "status": "Candidate",
      "evidence": [
        { "type": "Deployment", "reference": "auth-api v2.4.1 deployed 04:02 UTC" },
        { "type": "Log", "reference": "event e-77: 'invalid signature' at 04:05 UTC" },
        { "type": "Document", "reference": "runbook RB-014 §KeyRotation" }
      ],
      "unknowns": [ "Whether the signer pod recycled before/after deploy" ]
    }
  ],
  "evidence": [ { "id": "…", "type": "Log", "reference": "event e-77", "summary": "…" } ],
  "recommendedInvestigationSteps": [ "Compare JWT kid against deployed config", "Check signer pod rollout time" ],
  "recommendedRemediation": "Roll back auth-api to v2.4.0 and rotate the cache",
  "unknowns": [ "Full request volume at failure window" ]
}
```

### Analysis run (job resource)

```json
{
  "id": "…",
  "projectId": "…",
  "type": "ChangeRisk",
  "status": "RUNNING",
  "progress": { "step": "retrieval", "detail": "semantic search" },
  "result": { "kind": "RiskReport", "id": "…" },
  "model": "gemini-3.1-flash-lite",
  "promptVersion": "risk-v3",
  "startedAt": "…",
  "completedAt": null,
  "error": null
}
```

## 4. Authorization matrix (summary)

| Resource | Viewer | Engineer | Admin | Owner |
| --- | --- | --- | --- | --- |
| Read project data | ✅ (member) | ✅ | ✅ | ✅ |
| Submit changes / incidents / ingest | ❌ | ✅ | ✅ | ✅ |
| Run analyses / investigations / evaluations | ❌ | ✅ | ✅ | ✅ |
| Manage members / delete project | ❌ | ❌ | ✅ | ✅ |

Project membership is required for every project-scoped call ([ADR-0012](adr/0012-auth-model.md), [security-model.md](security-model.md)).

## 5. Async job semantics

1. `POST` returns `202` + `Location: /api/v1/analyses/{id}` (or `/evaluations/{id}`).
2. Job states: `Queued → Running → Succeeded | Failed`. Failed jobs carry a machine-readable `error.code` (e.g. `AI_VALIDATION_FAILED`, `LLM_RATE_LIMITED`, `RETRIEVAL_UNAVAILABLE`) and the AI-service error details are retained for the trace view.
3. Idempotency: `POST` bodies include a client-generated `requestId`; re-submission with the same `requestId` returns the existing job (no duplicate LLM spend).
4. Frontend polls with backoff (1s, 2s, 4s… cap 10s); no websockets in MVP.
