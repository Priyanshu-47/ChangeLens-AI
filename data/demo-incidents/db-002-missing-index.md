# INC-010: Payments list API slows to a crawl as data grows

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-3
- **Service:** acmepay-api
- **Archetype:** Database / schema migration
- **Difficulty:** Medium

## Symptom

`GET /api/v1/merchants/{id}/payments` (via `PaymentsRepository.GetByMerchantAsync`)
latency grew from 40ms to 4.8s over two months as the `payments` table crossed 40
million rows. The endpoint was used by the merchant dashboard, which started timing
out.

## Timeline

- Week 1 — p95 for the merchant payments endpoint drifts upward; no alert configured on this endpoint.
- Week 8 — Merchant dashboard reports timeouts on the payments tab.
- Week 8 — `EXPLAIN ANALYZE` shows a sequential scan on `payments` filtered by `merchant_id`; the `IX_Payments_MerchantId` index is missing in production.
- Week 9 — Index created in a maintenance window; p95 drops to 25ms.

## Root Cause

`PaymentDbContext.OnModelCreating` declares `entity.HasIndex(p => p.MerchantId)`,
so the index exists in the local schema. The production schema was created from an
earlier migration that predated the index declaration, and no later migration added
it — schema drift between EF's model and the deployed database.

## Resolution

- Added an explicit migration creating `IX_Payments_MerchantId`.
- Added a drift check to the deploy pipeline comparing EF model to the live schema.
- Configured latency alerts on the merchant payments endpoint.

## Lessons Learned

- EF's `HasIndex` in the model is a promise, not a migration — the schema is owned by migrations.
- Query latency alerts on hot read paths catch growth problems before dashboards fail.
