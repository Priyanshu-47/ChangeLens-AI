# Runbook: API Contract / Schema Mismatch

> **SYNTHETIC EVALUATION DATA** — demo runbook written for ChangeLens evaluation.

Applies to: partner-facing contracts on `acmepay-api` (payments, refunds, payouts).

## Symptoms

- Partner integrations fail with JSON deserialization errors (`Missing required property`).
- 400s from the gateway despite a request that used to work (external contract drift).
- Amounts off by 100x (unit change between dollars and cents).
- Refund endpoint 404s in one environment but not another (feature-flagged route).

## Diagnosis

1. Capture the exact request and response JSON; compare field-by-field with the OpenAPI spec.
2. Check the deploy history: a contract change (field removed, renamed, or unit changed) in the same major version is the usual cause.
3. For gateway 400s, diff the outgoing payload against the gateway's changelog.
4. For 404s, check `FeatureFlags` in the environment's appsettings before suspecting routing.

## Common Causes

- Field removed or renamed without a deprecation window (response contract).
- Units changed (cents → dollars) without a version bump (request contract).
- External API renamed a JSON field (`merchant_id` → `merchant_ref`).
- Route gated behind a feature flag that is off in the environment.

## Resolution

- **Removed field:** restore it serialized as null during the deprecation window; then follow a minor-version deprecation policy.
- **Unit change:** use `amount_cents` integers or explicitly annotated decimals; bump the major version for semantic changes.
- **External drift:** update `[JsonPropertyName]` attributes; add a recorded-response contract test.
- **Flag-gated route:** set the flag true in the environment and add a route-inventory smoke test.

## Rollback

- Revert the API change (restore fields / revert units) and redeploy — contract compatibility is binary.
- For external contract drift, pin the client to the gateway's previous endpoint version if available.
- Re-run the partner's failing integration test to confirm the contract matches before closing the ticket.
