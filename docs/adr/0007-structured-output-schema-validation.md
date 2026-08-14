# ADR-0007: Structured AI output with schema validation, bounded repair, safe failure

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

The brief is explicit: the main analysis results must never be uncontrolled prose. Free-form LLM output is not storable, not comparable, not evaluable, and not safely groundable.

## Decision

Every primary AI result (RiskReport, IncidentInvestigation, later tool-call proposals) is produced through a single pattern in the AI service:

1. The result schema is a **Pydantic model** — the single source of truth for both the JSON Schema sent to Gemini (`responseSchema`) and runtime validation.
2. Gemini generates against the schema; the response is parsed and validated with Pydantic (types, enums, ranges, cardinality).
3. **Bounded repair:** on failure, re-prompt with the exact validation errors instructing fixes only; max 2 repairs, each retried at most once.
4. **Safe failure:** still invalid → `422 AI_VALIDATION_FAILED` with attempt history. Unvalidated text is never returned or stored as a result.

Post-validation deterministic rules (no LLM) enforce: confidence in [0,1], enums, array bounds, and the **grounding rule** — every risk factor / root-cause candidate must reference ≥1 evidence id present in the input package; zero-evidence conclusions fail validation.

## Consequences

- Results are typed, storable, diffable, and evaluable; schema-validation-failure rate becomes a measured eval metric.
- The grounding rule mechanically enforces "evidence > claims" and exposes prompt-injection artifacts (a factor citing a fake id fails).
- Cost: schema versioning discipline (schema changes are versioned with prompts); bounded repair adds latency only on failure.
