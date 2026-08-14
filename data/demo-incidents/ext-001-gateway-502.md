# INC-005: Payments failing with 502 gateway_unavailable

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-1
- **Service:** acmepay-api
- **Archetype:** External API / integration
- **Difficulty:** Medium

## Symptom

At 10:30 UTC, `POST /api/v1/payments` began failing with
`502 gateway_unavailable` for all partners. Checkout success rate dropped to 0%.
The `ErrorHandlingMiddleware` log showed `PaymentGatewayException: Gateway unreachable`.

## Timeline

- 10:30 — All payment charges fail; the gateway's status page reports a regional incident.
- 10:35 — On-call confirms the failure is on the upstream side: `StripeGatewayClient` cannot connect to the gateway base URL.
- 10:40 — Retries kick in (`MaxRetries: 3`, 200ms backoff) but every attempt fails; the client surfaces 502 after exhausting retries.
- 11:05 — Gateway restores service; payments recover. The API never crashed.

## Root Cause

The external payment gateway had a regional outage. `StripeGatewayClient`
correctly classified the transport failure as retryable and exhausted its bounded
retries before surfacing `PaymentGatewayException("Gateway unreachable", 502)`.
`ErrorHandlingMiddleware` mapped that to a clean `502 gateway_unavailable` response.

## Resolution

- No code change required; upstream restored service.
- Added a circuit breaker at the gateway client boundary so the API can fail fast instead of burning 3 retries per request during a known outage.
- Added an alert on `502 gateway_unavailable` rate so incidents are detected at the edge, not via partner tickets.

## Lessons Learned

- Bounded retries are correct, but during a prolonged upstream outage they multiply load — a circuit breaker with a half-open state is the missing piece.
- A clean, well-typed 502 with `gateway_unavailable` let partners handle the failure programmatically instead of treating it as a checkout bug.
