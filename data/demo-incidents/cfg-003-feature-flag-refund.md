# INC-015: Refunds silently disabled in staging by a feature flag

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** Configuration / environment drift
- **Difficulty:** Easy

## Symptom

QA reported that the refund flow was "broken in staging": `POST .../refunds`
returned `404` even for valid payment IDs. The endpoint existed in production and
worked in the local environment.

## Timeline

- Monday — QA runs the refund test suite against staging; all refund requests 404.
- Tuesday — QA opens a ticket; the API team confirms the refund endpoint is registered and reachable via Swagger.
- Wednesday — A developer spots `appsettings.Staging.json`: `"FeatureFlags": { "EnableRefunds": false }`.
- Thursday — The feature flag is set to true in staging; refund tests pass.

## Root Cause

A feature flag (`EnableRefunds`) gates the refund route registration. The staging
override file sets it to `false` (defaulted when the file was created from an old
template), so the route was never mapped in staging. The API reported 404 — the
framework-correct response for an unmapped route, indistinguishable from a typo.

## Resolution

- Set `EnableRefunds: true` in staging.
- Added a route-inventory smoke test that asserts the refund endpoint exists per environment.
- Documented the flag in the runbook (`api-schema-mismatch.md`).

## Lessons Learned

- Feature-flagged routes surface as 404s, which look like routing bugs, not flags.
- An environment-config smoke test that asserts on endpoints saves a multi-day investigation.
