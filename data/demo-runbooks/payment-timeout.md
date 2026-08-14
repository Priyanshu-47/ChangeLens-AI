# Runbook: Payment Gateway Timeout / Unavailability

> **SYNTHETIC EVALUATION DATA** — demo runbook written for ChangeLens evaluation.

Applies to: `acmepay-api` → `StripeGatewayClient` / `PayoutGatewayClient`.

## Symptoms

- `502 gateway_unavailable` — transport failure to the gateway (DNS, connect, TLS).
- `504 Gateway request timed out` — `TaskCanceledException` surfaced as a typed gateway error.
- `PaymentGatewayException` with a 5xx or 429 status code.
- Elevated charge latency (p99 approaching the 10s timeout).
- Checkout success rate dropping while the API itself looks healthy.

## Diagnosis

1. Distinguish client vs upstream: check the gateway status page and the client logs (they record attempt counts and backoff).
2. Check `Resilience`/`PaymentGateway` config: `MaxRetries`, `TimeoutSeconds`, `BaseBackoffMilliseconds`.
3. Check for retry amplification: retries per request × requests in flight. If the gateway is throttling (429), look at `Retry-After` handling.
4. Confirm idempotency: charges carry an `IdempotencyKey`; verify retries reuse it (see INC-017).

## Common Causes

- Upstream regional outage or degraded worker pool.
- Client timeout shorter than upstream p99.
- Misconfigured base URL or rotated API key for the gateway.
- Retry storm amplifying a brief upstream blip.

## Resolution

- **Upstream outage:** no code change; monitor and communicate. Optionally enable the circuit breaker to fail fast.
- **Timeout too tight:** raise `PaymentGatewayOptions.TimeoutSeconds` above measured upstream p99 (e.g., 15s).
- **Throttling:** honor `Retry-After`, add jitter, and cap concurrent in-flight requests.
- **Duplicate charges:** persist the idempotency key before the first attempt and reuse it on retries.

## Rollback

- Temporarily set `MaxRetries` to 0 via config to stop retries during an upstream incident (shed load).
- If a config change caused the failure, revert the `PaymentGateway` section and redeploy.
- Re-run the payout batch or affected charges after the gateway recovers.
