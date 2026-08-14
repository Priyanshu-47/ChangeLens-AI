# INC-003: Refund endpoint returns 403 for a partner that used to be allowed

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** Authentication / authorization
- **Difficulty:** Medium

## Symptom

Partner "acme-partner-2" began receiving `403 refunds_not_allowed` on
`POST /api/v1/payments/{id}/refunds`. Payments and refunds worked for every other
partner, and nothing was deployed at 14:00 when the failures started.

## Timeline

- 13:55 — A configuration update changed the partner tier for `partner-2` from "full" to "standard".
- 14:00 — Refund requests from `partner-2` start returning `403 refunds_not_allowed`.
- 14:40 — The partner opens a ticket: "Refunds suddenly forbidden, no warning".
- 15:10 — On-call finds the tier change and that `ApiKeyValidator` maps the standard tier to `CanRefund: false`.
- 15:30 — The tier is reverted; refunds resume.

## Root Cause

`ApiKeyValidator` returns an `ApiKeyPrincipal` with a `CanRefund` flag per partner.
`ApiKeyAuthMiddleware` enforces that flag on paths ending in `/refunds`. The partner
tier downgrade was a billing decision, but the API behavior (hard 403 with no notice
period) was not part of the change plan. The middleware correctly enforced the new
permission — the failure was a process gap, not a code bug.

## Resolution

- Reverted the tier change pending a migration plan.
- Added a grace-period concept: tier downgrades are staged as "read-only" before full revocation.
- Exposed `CanRefund` in the partner self-service portal so permission state is visible.

## Lessons Learned

- Authorization state lives in more than one place (billing system + `ApiKeyValidator` seed data) — keep them synchronized or make the API read from the source of truth.
- A 403 should include enough context (`refunds_not_allowed`) to distinguish policy from a bug.
