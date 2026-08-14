# INC-017: Customer charged twice after a timeout retry

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-2
- **Service:** acmepay-api
- **Archetype:** Retry / timeout / resilience
- **Difficulty:** Ambiguous

## Symptom

A customer's card was charged twice for one checkout. Support found two `Payment`
records with different IDs, both `Succeeded`, referencing the same gateway charge
amount, created 40 seconds apart. Both succeeded at the gateway.

## Timeline

- 13:00 — Checkout request times out at the API (10s).
- 13:00 — `StripeGatewayClient` retries with a *new* idempotency key because the request's key was lost when the first attempt threw.
- 13:01 — Both attempts succeed at the gateway; the customer is charged twice.
- 13:20 — Support detects the double charge from the card statement.
- 15:00 — Root cause confirmed: the retry path generated a fresh `IdempotencyKey` (`Guid.NewGuid()`) instead of reusing the original, defeating the gateway's deduplication.

## Root Cause

`StripeGatewayClient.AuthorizeAsync` generates the idempotency key at request time
and reconstructs it on retry: `idempotencyKey ?? Guid.NewGuid()`. The `??` means a
retry after a transport failure — when the original key was never persisted — sends
a *different* key. The gateway's idempotency guarantee is keyed on that value, so
both attempts were treated as distinct charges.

## Resolution

- Moved idempotency-key generation to the caller (`ProcessPaymentHandler`) and persisted it with the `Payment` row before the first gateway call.
- Retries now reuse the persisted key unconditionally.
- Added a test that simulates a timeout on the first attempt and asserts a single gateway charge.

## Lessons Learned

- Idempotency keys must be stable across retries; generating them inside a retried callable is the classic footgun.
- The ambiguity: was this a timeout bug, a retry bug, or a missing-persistence bug? The fix spans all three layers.
