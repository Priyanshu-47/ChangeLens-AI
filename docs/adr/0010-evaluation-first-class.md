# ADR-0010: Evaluation is a first-class feature with a golden dataset

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

AI features without measurement are unverifiable claims. The brief demands a golden dataset, strategy comparison (keyword vs vector-only vs hybrid vs full pipeline), measured metrics, and a ban on fabricated results. Evaluation also gates model/prompt changes.

## Decision

Evaluation is a product feature, not an internal script:

- **Golden dataset** versioned in `data/` (15–25 MVP cases): code changes, incidents, expected impacted components, expected related incidents, expected test scenarios, expected root-cause candidates.
- **Runner** (`POST /internal/v1/evaluations/run`) executes strategies against the dataset with a `limit` guard; results persist in `evaluation_runs` + the ai schema and are exposed via the public API.
- **Metrics measured, never invented:** Recall@K, precision, MRR, groundedness, hallucination rate, latency, token usage, estimated cost, schema-validation failures — each labeled with dataset size and date.
- **Comparison view** (Phase 7 UI) shows strategy-vs-strategy results; CI (Phase 9) runs evaluation as a regression gate on prompt/model changes (offline strategies in CI, real-LLM slice optional/manual).

## Consequences

- Dashboard shows only real numbers; every shipped prompt or model change is verifiable against the dataset.
- Cost: dataset curation effort; eval runs consume tokens (bounded by `limit`, cost-labeled); honest framing means the README reports actual dataset scope, not a false "industry benchmark".
