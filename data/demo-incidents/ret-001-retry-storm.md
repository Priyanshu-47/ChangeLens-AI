# INC-016: Retry storm after a brief gateway blip

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-2
- **Service:** acmepay-api
- **Archetype:** Retry / timeout / resilience
- **Difficulty:** Medium

## Symptom

A 90-second gateway blip caused a 30-minute retry storm. `StripeGatewayClient`
retried every failed charge, but each retry itself timed out, and the API's total
charge volume (including retries) hit 14x normal. The gateway's status page showed
no issue after the first two minutes.

## Timeline

- 12:00 — Gateway blip (2 minutes of 5xx and timeouts).
- 12:02 — Gateway healthy again, but thousands of retries are still in flight from the blip window.
- 12:05 — Charge volume peaks at 14x normal; gateway latency rises again under the load.
- 12:20 — The retry backlog drains; volume normalizes.
- 12:30 — On-call analysis: the retry policy (3 retries, 200ms * attempt backoff) was per-request, not coordinated, so the tail of the blip multiplied every in-flight request.

## Root Cause

Per-request retries with fixed backoff are correct for isolated failures but act as
a load multiplier under a shared upstream failure. The first wave of requests all
failed at once and all retried at nearly the same time (no jitter), synchronizing
into a storm. Retry and load-shedding policies were designed independently.

## Resolution

- Added jitter to the backoff to de-synchronize retry waves.
- Added a circuit breaker shared across requests: after N failures in a window, fail fast for a cooldown period.
- Added a global retry-rate limiter so retries never exceed 2x the baseline request rate.

## Lessons Learned

- Retry policy must be designed against *shared* failures, not just per-request failures.
- Synchronized retries are worse than no retries; jitter and circuit breakers are the minimum viable defense.
