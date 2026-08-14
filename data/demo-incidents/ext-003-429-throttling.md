# INC-007: Gateway throttles AcmePay with 429s after a traffic spike

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-2
- **Service:** acmepay-api
- **Archetype:** External API / integration
- **Difficulty:** Ambiguous

## Symptom

After a marketing campaign drove a 4x traffic spike, the gateway began returning
`429` on a growing share of charge requests. `StripeGatewayClient` treats 429 as
transient and retries with a fixed 200ms * attempt backoff. Instead of recovering,
the 429 rate climbed from 2% to 18% over 40 minutes.

## Timeline

- 14:00 — Traffic spike begins; gateway 429s start at 2%.
- 14:20 — 429s reach 10%; retries are firing on every failed request.
- 14:30 — 429s reach 18%; checkout success drops to 92%.
- 14:45 — On-call recognizes a retry amplification loop: each retry re-enters the gateway's token bucket, consuming more quota.
- 15:00 — Retry policy changed to honor `Retry-After` and cap concurrent in-flight gateway requests; 429 rate falls back to 2%.

## Root Cause

429 means "slow down", not "try again immediately". The client's fixed backoff did
not respect the gateway's `Retry-After` header and the API issued retries from a
hot path, amplifying the throttling. The gateway's token bucket refilled on its own
schedule; aggressive retries kept the bucket empty.

## Resolution

- Read and honor the `Retry-After` header on 429 responses.
- Added a per-client semaphore limiting concurrent in-flight gateway requests.
- Added jitter to backoff to avoid synchronized retry waves.

## Lessons Learned

- 429s must be distinguished from 5xx: both are retryable, but only 429 carries an explicit retry hint.
- Retry policy without load shaping can turn a transient condition into a sustained one.
- The ambiguity: the client "did the right thing" by retrying; the failure was the missing backoff discipline.
