# INC-012: Payment status shows wrong value in the merchant dashboard

> **SYNTHETIC EVALUATION DATA** — this incident is fictional and written for ChangeLens evaluation only.

- **Severity:** SEV-4
- **Service:** acmepay-api
- **Archetype:** Database / schema migration
- **Difficulty:** Easy

## Symptom

Some payments that succeeded at the gateway displayed as "Pending" in the merchant
dashboard. The discrepancy appeared only for payments created after a deploy that
changed how `PaymentStatus` is stored.

## Timeline

- Monday — Deploy changes `PaymentStatus` persistence from an integer column to a string (`HasConversion<string>()`).
- Tuesday — Merchants report "Pending" payments that were actually charged.
- Wednesday — Support confirms the affected rows have `status = 'Succeeded'` in the database but the API returns `Pending`.
- Thursday — Root cause: the deploy's data migration converted the old integer values with a lookup table that had a typo — value `1` (Succeeded) was mapped to the string `"Pending"`.

## Root Cause

The entity changed from `status INT` to `status VARCHAR(32)` and EF's
`HasConversion<string>()` writes the enum *name*. The accompanying data migration
mapped legacy integer values to names using a lookup that contained a copy-paste
error for value `1`. The API faithfully read the wrong string back.

## Resolution

- Fixed the lookup and corrected the affected rows with a corrective migration.
- Added a reconciliation check that compares DB status against gateway charge status nightly.
- The enum conversion itself was correct; the data migration was the defect.

## Lessons Learned

- Enum-to-string conversions are safe; hand-written value mapping tables in data migrations are where typos hide.
- Add a reconciliation job whenever status semantics change; it turns silent drift into an alert.
