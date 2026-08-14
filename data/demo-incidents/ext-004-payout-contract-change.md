# INC-008: Payouts start failing after the gateway renames a JSON field

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** External API / integration
- **Difficulty:** Medium

## Symptom

The weekly payout batch began failing with `Payout rejected with status 400`.
No AcmePay code changed that day. The batch had run successfully the previous week.

## Timeline

- 02:00 — Weekly payout batch runs; every `POST /v1/payouts` returns 400.
- 02:30 — Batch job marks payouts failed and alerts.
- 09:00 — On-call compares the request payload against the gateway's changelog: the gateway renamed `merchant_id` to `merchant_ref` in its v1 contract, deprecated the old field, and began rejecting requests that omit the new one.
- 10:00 — `PayoutGatewayClient` updated to send `merchant_ref`; batch re-runs successfully.

## Root Cause

The gateway made a breaking field rename in its API contract and enforced it
without a grace period. `PayoutGatewayClient` serializes `PayoutRequest` with
`[JsonPropertyName("merchant_id")]`, which no longer matches the upstream contract.
`PayoutGatewayClient` classified the 400 as non-retryable and failed the batch
immediately — correct behavior, wrong contract.

## Resolution

- Updated the JSON property names in `PayoutRequest` to match the new contract.
- Added a contract test that replays a recorded gateway response and asserts deserialization succeeds.
- Subscribed to the gateway's API changelog feed so contract changes are noticed before the batch runs.

## Lessons Learned

- External contract drift is invisible until runtime; recorded-response contract tests catch it in CI, not at 2am.
- 400s from an integration point are contract bugs until proven otherwise.
