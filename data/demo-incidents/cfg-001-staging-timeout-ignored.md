# INC-013: Staging configuration change has no effect — drift in plain sight

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** Configuration / environment drift
- **Difficulty:** Ambiguous

## Symptom

A config change to staging — tightening the gateway timeout from 10s to 5s and
retries from 3 to 1 — was applied to `appsettings.Staging.json` and deployed.
Nothing changed: staging still used 10s timeouts and 3 retries.

## Timeline

- 09:00 — Engineer edits `appsettings.Staging.json` (timeout 5s, retries 1) to test failure behavior; deploys staging.
- 09:30 — Test shows timeouts still at 10s and three retry attempts in logs.
- 10:00 — Engineer discovers `StripeGatewayClient` reads `PaymentGatewayOptions` (the `PaymentGateway` section), while the edited file overrides the `Resilience` section bound to `ResilienceOptions` — a class nothing in the charge path reads.
- 10:15 — Fix: `PaymentGatewayOptions` bound from the same section; config takes effect.

## Root Cause

Two option classes exist: `PaymentGatewayOptions` (read by the gateway client) and
`ResilienceOptions` (bound in DI but never consumed by the retry path). The staging
file was written against the `Resilience` section, so the change silently targeted
a dead configuration tree. No error was raised; the value simply had no consumer.

## Resolution

- Bound `PaymentGatewayOptions` to the `Resilience` section name used by the environment files (single source of truth).
- Removed the unused `ResilienceOptions` registration.
- Added a startup diagnostic that logs every bound option section and its consumer.

## Lessons Learned

- Configuration that nothing reads is worse than no configuration — it creates the illusion of control.
- The ambiguity: the "bug" could be framed as config error, code error (two option classes), or process error (no test asserted the value took effect).
