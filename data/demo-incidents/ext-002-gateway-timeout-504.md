# INC-006: Intermittent 504 "Gateway request timed out" on charges

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-2
- **Service:** acmepay-api
- **Archetype:** External API / integration
- **Difficulty:** Medium

## Symptom

About 3% of payment charges returned `504 Gateway request timed out` from
`StripeGatewayClient`. The failures were intermittent: a retry usually succeeded.
Average charge latency was normal, but the p99 latency spiked to 12s during the
window.

## Timeline

- 08:00 — Alerts fire on p99 latency and `PaymentGatewayException` with status 504.
- 08:30 — On-call reviews the gateway client code and finds the timeout is 10 seconds, configured via `PaymentGatewayOptions.Timeout`.
- 09:00 — Gateway-side investigation shows the upstream had a degraded worker pool during the same window; its own p99 response time was 9.4s.
- 09:20 — The gateway team scales workers; timeouts stop.

## Root Cause

The upstream gateway had a degraded worker pool, pushing its p99 response time
above the 10s client timeout. `StripeGatewayClient` correctly retried, and most
retries landed after the upstream recovered, so the user-visible failure rate was
only ~3%. The `504` is a faithful mapping of `TaskCanceledException` to a typed
gateway error.

## Resolution

- Upstream scaled workers; timeouts ceased.
- Post-incident: raise the charge timeout to 15s and add per-endpoint timeout configuration rather than a single global value.
- Added a dashboard for gateway p50/p99 vs client timeout so drift is visible before it becomes an incident.

## Lessons Learned

- Timeout is a contract between client and upstream; tune it from measured upstream p99, not from a default.
- Retrying a timeout is correct only if the operation is idempotent — charges carry an `IdempotencyKey`, so retries are safe here.
