# Runbook: Database Connection Pool Exhaustion

> **SYNTHETIC EVALUATION DATA** — demo runbook written for ChangeLens evaluation.

Applies to: `acmepay-api` → PostgreSQL (Npgsql pool).

## Symptoms

- `Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool`.
- API failures persist briefly after the database becomes reachable again.
- Database CPU/memory look fine while the API errors — the pool, not the DB, is the bottleneck.
- Errors spike after a partial network partition or a slow external call.

## Diagnosis

1. Confirm the pool is the failure point: the error text names the pool explicitly.
2. Check `Max Pool Size` config and the active-connection metric during the incident.
3. Look for database work performed *while holding a connection and awaiting an external call* (gateway, cache) — that combination drains pools fast.
4. Verify the pool refills after recovery: Npgsql refills lazily, so a short grace period after the DB recovers is expected.

## Common Causes

- Long external calls inside a DB transaction or while a connection is held.
- A single slow query (missing index) holding connections far past the mean.
- Connection string pointed at the wrong host (connection attempts to a dead host occupy pool slots).
- Sudden traffic spike with a small default pool (100).

## Resolution

- **Hold time:** move external calls outside the transaction/connection scope (see INC-011).
- **Pool size:** set `Max Pool Size` explicitly based on max concurrency × per-request connections.
- **Slow query:** find the sequential scan via `EXPLAIN ANALYZE` and add the missing index.
- **Traffic shaping:** cap concurrent requests at the API layer so the pool is never the first resource to exhaust.

## Rollback

- Restart the API to reset the pool if it is wedged (connections held by abandoned requests).
- Temporarily raise `Max Pool Size` via config and redeploy — but treat this as a stopgap, not a fix.
- If a query change caused the regression, revert that change first.
