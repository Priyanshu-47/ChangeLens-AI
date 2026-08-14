# Runbook: Authentication / Authorization Failures

> **SYNTHETIC EVALUATION DATA** — demo runbook written for ChangeLens evaluation.

Applies to: `acmepay-api` (API-key middleware, JWT service tokens, partner API keys).

## Symptoms

- `401 api_key_missing` — the `X-Api-Key` header is absent.
- `401 api_key_invalid` — the header is present but the key is unknown.
- `403 refunds_not_allowed` — the key is valid but the partner tier lacks refund permission.
- `401 invalid_signature` on service-to-service calls — JWT validation failed.
- A working integration suddenly fails after a key rotation or config deploy.

## Diagnosis

1. Confirm the failing path: `/api/v1/payments/*` requires a partner API key; service tokens are used internally.
2. Check the `ApiKeyAuthMiddleware` log line: it records whether the header was present (`auth.header_present`).
3. For `invalid_signature`: check the token issuer, audience, and `Auth:JwtSigningKey`. If a rotation happened recently, verify the previous key is still in the historical keys list.
4. For `refunds_not_allowed`: check the partner's `CanRefund` flag in `ApiKeyValidator` and the billing-tier source of truth.

## Common Causes

- Header injection rule lost in an edge proxy or load balancer.
- JWT signing key rotated without keeping the previous key for the overlap window.
- Partner tier change without a grace period.
- API key copied with a trailing newline or space.

## Resolution

- **Missing header:** have the caller re-add the header at their edge; verify with a request that includes `X-Api-Key`.
- **Invalid key:** issue a new key and confirm the value byte-for-byte.
- **Key rotation fallout:** re-add the previous `Auth:JwtSigningKey` to the history list and restart; allow in-flight tokens to expire before removing it.
- **Permission denied:** confirm the tier change was intentional; revert or grant a grace period per policy.

## Rollback

- Revert the config change (key, tier, or flag) and redeploy.
- For signature issues, restoring the old key requires no code change — only configuration.
- Verify with the same failing request that the error code changes before the status becomes 2xx.
