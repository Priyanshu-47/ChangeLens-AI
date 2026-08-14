# Runbook: Database / Schema Migration Failures

> **SYNTHETIC EVALUATION DATA** — demo runbook written for ChangeLens evaluation.

Applies to: `acmepay-db` (PostgreSQL), EF Core migrations in `AcmePay.Infrastructure`.

## Symptoms

- Deploy pipeline fails at the migration step.
- `NOT NULL constraint failed` / `ALTER TABLE ... ADD COLUMN ... NOT NULL` errors.
- Queries on hot tables degrade after a deploy (missing index drift).
- Status values wrong after an enum/conversion change.

## Diagnosis

1. Read the failing migration output; `dotnet ef database update` prints the exact SQL.
2. Distinguish schema failure (constraint, type) from data failure (backfill, duplicates).
3. Check whether the table has historical rows — most failures only reproduce with production-shaped data.
4. For performance regressions: `EXPLAIN ANALYZE` the hot query and compare the EF model (`HasIndex`) to the live schema.

## Common Causes

- Non-nullable column added without a default or backfill.
- Migration that works on an empty schema but fails on production data.
- Model declares an index but no migration created it (schema drift).
- Hand-written value mapping in a data migration with a typo.

## Resolution

- **Non-nullable column:** convert to three steps — `ADD COLUMN` (nullable) → backfill `UPDATE` → `ALTER COLUMN SET NOT NULL`.
- **Missing index:** add an explicit migration `CREATE INDEX`, not just a model declaration.
- **Enum conversion:** keep the conversion in EF (`HasConversion<string>()`), but audit the data migration's lookup table.
- **Failed mid-flight:** roll back the deploy (migrations are transactional in EF), fix, and re-apply.

## Rollback

- `dotnet ef database update <previous>` reverts the last migration.
- For irreversible migrations (dropped columns), restore from the pre-migration backup and re-apply in a maintenance window.
- Verify with a `SELECT` on the affected table before re-opening traffic.
