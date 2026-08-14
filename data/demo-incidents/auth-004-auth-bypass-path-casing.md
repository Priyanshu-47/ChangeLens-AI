# INC-004: Security review flags a possible auth bypass via path casing

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-1 (potential)
- **Service:** acmepay-api
- **Archetype:** Authentication / authorization
- **Difficulty:** Ambiguous

## Symptom

A routine security review found that `ApiKeyAuthMiddleware` matches the request
path with `StringComparison.OrdinalIgnoreCase`, while ASP.NET Core's routing
normalizes the path differently. The reviewer could not prove an exploit, but the
two normalizations do not always agree.

## Timeline

- Day 1 — Security review of `ApiKeyAuthMiddleware` notes the casing discrepancy and files a finding.
- Day 3 — Pen-test attempt: `POST /API/v1/Payments/...` bypasses the middleware's prefix check but routes to the same controller action.
- Day 4 — Reproduced in a local sandbox: requests with uppercase path segments reach `PaymentsController` without passing the API-key check.
- Day 10 — Fix merged: middleware now uses the same path normalization as routing and validates the key on all `/api` routes.

## Root Cause

The middleware used its own prefix match (`StartsWith("/api/v1/payments")`) with
case-insensitive comparison, but ASP.NET Core routing treats the path case- and
trailing-slash-insensitively in a slightly different way. The two rules diverged on
edge-case paths, allowing unauthenticated requests to reach protected actions.

## Resolution

- Replaced the custom prefix logic with ASP.NET Core endpoint metadata (`RequireApiKey` attribute on actions).
- Added integration tests that exercise uppercase, mixed-case, and trailing-slash paths against the protected endpoints.
- The pen-test finding was downgraded to resolved after the fix shipped and the new tests passed.

## Lessons Learned

- Middleware path matching is a second routing implementation and drifts from framework routing.
- Security-critical path logic should be declarative (endpoint metadata), not string matching.
- The ambiguity: no production exploitation was proven, but the fix was cheap and removed the risk class entirely.
