# INC-019: Refund amounts off by 100x after a contract migration

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-1
- **Service:** acmepay-api
- **Archetype:** API contract / schema changes
- **Difficulty:** Medium

## Symptom

A partner refunding $50.00 was refunded $0.50 (and, for another merchant, $5,000.00).
The API accepted the request, the gateway processed it, and the wrong amount reached
the customer's card. The `refund` payload had changed units without a version bump.

## Timeline

- 08:00 — New partner integrates against `POST /api/v1/payments/{id}/refunds`.
- 08:30 — Partner's first real refund: $0.50 instead of $50.00.
- 09:00 — Support escalates; the partner's integration sends `"amount": 50` expecting cents (the documented contract), but the API interprets it as dollars.
- 09:30 — API team confirms: the refund contract was migrated from "amount in cents (integer)" to "amount in dollars (decimal)" in the same major version; the old partner (sending dollars) is now wrong by 100x.
- 10:00 — Decision: version the endpoint (`/v2/refunds` with explicit `amount_cents`), keep `v1` dollar semantics, and publish a migration notice.

## Root Cause

A breaking semantic change to the refund request contract — units changed from
cents to dollars — was released inside the same major version with no detection.
`RefundPaymentCommand.Amount` is a `decimal` with no unit annotation, so the change
was invisible to both sides. Partners cannot distinguish "amount" in cents vs
dollars from the schema alone.

## Resolution

- Reverted to cents semantics on the existing endpoint; added `/v2` with explicit `amount_cents` and `amount` removed.
- Added an OpenAPI annotation (`units: cents`) and a contract test asserting the unit of `Amount` on both endpoints.
- Published a changelog; affected refunds were corrected via support flow.

## Lessons Learned

- Unit changes are breaking changes even when the JSON shape is unchanged.
- Money should be transmitted in the smallest unit (`amount_cents` integer) or explicitly annotated — never a bare decimal.
