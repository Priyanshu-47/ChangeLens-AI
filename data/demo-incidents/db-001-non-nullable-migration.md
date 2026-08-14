# INC-009: Deploy fails — migration adds a non-nullable column to a hot table

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-2
- **Service:** acmepay-db
- **Archetype:** Database / schema migration
- **Difficulty:** Medium

## Symptom

A routine deployment to `acmepay-api` failed during the database migration step.
`dotnet ef database update` reported a `NOT NULL constraint failed` on the
`payments` table. The API was never taken down, but the deployment pipeline
blocked all further releases for the morning.

## Timeline

- 09:00 — Release train starts; EF Core migration `AddGatewayTxnId` is applied.
- 09:01 — Migration fails: `ALTER TABLE payments ADD COLUMN gateway_txn_id TEXT NOT NULL` cannot complete because existing rows have NULL.
- 09:20 — Rollback: migration reverted, deploy marked failed.
- 10:00 — Fixed migration: add column as nullable, backfill from gateway records, then add the NOT NULL constraint in a follow-up migration.

## Root Cause

The migration added a `NOT NULL` column without a default or backfill. PostgreSQL
rejects the `ALTER TABLE` because existing rows violate the constraint immediately.
The failure is deterministic and reproducible locally — the team had simply never
run the migration against a database with historical rows.

## Resolution

- Rewrote the migration in three safe steps: `ADD COLUMN` (nullable) → `UPDATE` backfill → `ALTER COLUMN SET NOT NULL`.
- Added a rule: every migration must run against a snapshot of production-shaped data in CI before release.
- Documented the pattern in the database-migration runbook (`database-migration.md`).

## Lessons Learned

- Schema migrations are data operations; test them against data, not an empty schema.
- A failed migration should abort the deploy before traffic is cut over — the pipeline did this correctly.
