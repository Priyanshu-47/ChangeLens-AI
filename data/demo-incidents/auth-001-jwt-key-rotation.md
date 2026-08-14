# INC-001: Sudden 401s after JWT signing key rotation

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-2
- **Service:** acmepay-api
- **Archetype:** Authentication / authorization
- **Difficulty:** Easy

## Symptom

At 09:14 UTC, partners started receiving `401 invalid_signature` responses from
`POST /api/v1/payments`. The error rate for authenticated endpoints jumped from
0.1% to 22% within five minutes. The API itself was healthy: CPU, memory, and
database latency were all normal. Only calls carrying a JWT were affected.

## Timeline

- 08:55 — Ops rotated the `Auth:JwtSigningKey` value in the configuration store as part of a scheduled key-rotation task.
- 09:14 — First partner reports of 401s. The on-call engineer checks the API logs and sees `SecurityTokenInvalidSignatureException` in `TokenService` validation paths.
- 09:40 — Engineer notices the rotation script updated the active signing key but did NOT add the previous key to the historical key list.
- 10:05 — Previous key is added back to the `Jwt:SigningKeys` (history) list and the service is restarted.
- 10:12 — Error rate returns to baseline.

## Root Cause

`TokenService` issues tokens signed with the current `Auth:JwtSigningKey`. When a
key is rotated, in-flight tokens signed with the old key become invalid immediately
unless the old key is retained in the historical keys list until all issued tokens
expire. The rotation runbook (`authentication-failure.md`) documents this, but the
automation only replaced the active key and skipped the history step.

## Resolution

- Added the previous signing key to the historical keys list and restarted the API.
- Updated the key-rotation automation to verify the history list contains the previous key before cutting over.
- Added an alert that fires when `invalid_signature` errors exceed 5% over 5 minutes.

## Lessons Learned

- Key rotation is a two-step operation: add the new key, keep the old key valid for overlap.
- In-flight token lifetime (15 minutes) must be smaller than the overlap window.
- A monitoring alert on signature-validation failures would have caught this within minutes.
