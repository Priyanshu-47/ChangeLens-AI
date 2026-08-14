# INC-011: Database connection pool exhaustion during a partial outage

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-1
- **Service:** acmepay-api
- **Archetype:** Database / schema migration
- **Difficulty:** Ambiguous

## Symptom

During a 25-minute partial network partition, `acmepay-api` started failing with
`Timeout expired. The timeout period elapsed prior to obtaining a connection from
the pool`. After the network recovered, the API kept failing for another 20 minutes
even though the database was reachable.

## Timeline

- 10:00 — Network partition between the API tier and PostgreSQL.
- 10:01 — Every `SaveChangesAsync` blocks waiting for a pooled connection.
- 10:05 — Pool (default 100) exhausted; new requests throw pool-timeout immediately.
- 10:25 — Network recovers; database is healthy, but the pool is still drained: connections held by requests that are themselves waiting on the pool.
- 10:45 — Connections slowly release as requests time out; service recovers without intervention.

## Root Cause

Npgsql's default connection pool has a hard cap. During the partition, in-flight
requests held connections while waiting on the database; the pool drained, and
new requests failed instantly. After recovery, the pool refills lazily and the
API must shed load before steady state returns. No code defect — an operational
characteristic of pooled connections under partial failure.

## Resolution

- Reduced connection hold time by moving long external calls (gateway) out of the DB transaction scope.
- Configured `Max Pool Size` explicitly and added pool-exhaustion metrics.
- Documented the recovery pattern: after a partition, allow the pool to warm up before resuming full traffic.

## Lessons Learned

- Pool exhaustion symptoms look like database outages but are often a symptom of holding connections during external calls.
- The ambiguity: is the fix code (hold fewer connections), configuration (bigger pool), or operations (traffic shaping)? The answer is usually all three.
