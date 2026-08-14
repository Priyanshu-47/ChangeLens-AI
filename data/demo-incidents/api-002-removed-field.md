# INC-020: Partner dashboard breaks when a response field is removed

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** API contract / schema changes
- **Difficulty:** Easy

## Symptom

A partner's reconciliation job started throwing JSON deserialization errors after a
routine AcmePay deploy. The partner's code read `decline_reason` from the payment
response; the field was no longer present.

## Timeline

- 10:00 — AcmePay deploys a cleanup that removes `decline_reason` from the payment response for declined payments (it was always null).
- 10:15 — Partner's reconciliation job begins failing: `Missing required property: decline_reason`.
- 11:00 — Partner opens a ticket with the exact JSON and field name.
- 11:30 — AcmePay adds the field back as `"decline_reason": null` for compatibility and schedules a proper deprecation.

## Root Cause

The cleanup removed a response field that internal consumers had already stopped
using, but the partner's strict deserializer (`Required` property) treats a missing
field as a hard failure. The field was part of the de-facto contract even though it
was always null in practice.

## Resolution

- Restored the field (null-valued) on the response.
- Added a response-contract snapshot test that pins every public field of `PaymentResult`.
- Deprecation policy: announce removals one minor version ahead, and keep fields serialized as null during the deprecation window.

## Lessons Learned

- Response fields are contract even when always null; removing them is a breaking change for strict clients.
- Snapshot contract tests make unintentional field removals fail CI instead of a partner's pipeline.
