# Evaluation & AI Observability (Phase 7)

This document defines how ChangeLens measures retrieval, dependency analysis,
structured output, and grounding — and how every analysis is traceable back to the
evidence it used. It is the reference for [ADR-0010](adr/0010-evaluation-first-class.md)
(update that record) and for the `app/evaluation` package.

## 1. Goals

The evaluation framework answers, deterministically and without an LLM judge:

1. Did retrieval find relevant evidence? → Recall@K / Precision@K / MRR / Hit Rate.
2. What does each retrieval leg (vector / keyword / dependency) contribute? → per-leg
   ablation + hybrid-contribution attribution.
3. Did the AI output valid structured data? → schema validity.
4. Was the output grounded? → grounding validity (mechanical, never an LLM judge).
5. Did the AI cite the correct evidence? → evidence coverage over the gold sources.
6. How long did each stage take, and why did an analysis fail? → per-stage trace.
7. Can an analysis be reproduced? → dataset version + full run configuration.

**Honesty rule (brief §18):** only measured numbers appear in reports. No metric is
reported for a case whose annotations cannot support it — such cases are *skipped with a
reason*, never silently counted as zero.

## 2. Golden dataset

`data/golden-dataset/cases.json` — 20 synthetic cases, **dataset version v1** (top-level
`version` field; bump it when cases change). Each case:

| Field | Meaning |
| --- | --- |
| `id` | stable case id (`case-001` …) |
| `query` | the natural-language query used for retrieval |
| `expected_evidence` | gold source keys (file basenames / markdown file names) |
| `archetype` / `difficulty` / `notes` | grouping metadata (auth, external, db/schema, config, retry/timeout, api-contract; easy/medium/ambiguous) |

The dataset is retrieval-focused: gold annotations name *documents*, not chunks. The
loader (`app/evaluation/dataset.py`) requires only `id`, `query`, `expected_evidence`;
missing `version` defaults to `"unknown"`; malformed entries are skipped (reported), not
fatal.

### 2.1 Relevance mapping (document-level)

Retrieved chunks map to gold keys mechanically: `basename(path)` (code files) or the
file name (markdown incidents/runbooks), falling back to the document title. Matching is
exact-first then case-insensitive — there is **no semantic similarity judgment anywhere
in the evaluator**. Multiple chunks of the same gold document are **one hit** (deduped,
first rank wins), so Recall@K can never exceed 1.0.

## 3. Metrics (exact formulas)

All metrics are computed from a ranked list of retrieved, deduped source keys
(`retrieved`) against the gold set (`relevant`).

```
Recall@K      = |{i in retrieved[:K] : i in relevant}| / |relevant|        [0, 1]
Precision@K   = |{i in retrieved[:K] : i in relevant}| / K                 [0, 1]
MRR           = 1 / rank(first i in retrieved with i in relevant), else 0 [0, 1]
HitRate@K     = 1 if any gold item in retrieved[:K], else 0               {0, 1}
```

- **Precision@K** assumes the top-K list is fully judged — true here (every retrieved
  chunk is compared against the gold set). It is only reported for cases with gold
  annotations.
- **MRR** assumes a ranked list — always true.
- A case with no gold annotations is skipped with the reason `no gold evidence
  annotations for this case`; a dependency leg with no derivable identifiers is skipped
  with `no dependency terms derivable from the query` (the dependency leg is
  change-model-driven; a bare query cannot exercise it).
- Aggregates are the mean over evaluated cases; `None` when there are no values (never
  fabricated zeros).

**Leg contribution** is attribution, not quality: the fraction of hybrid top-K items
whose `sources` show the leg (vector score present / keyword rank present / dependency
rank present). It answers "how often did this leg surface a chunk that reached the
fused result".

## 4. Running the evaluation

```bash
cd ai-service
DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens" \
  ./.venv/Scripts/python -m app.evaluation.run
```

- **Deterministic by construction:** the CLI forces `AI_PROVIDER=mock` and
  `EMBEDDING_PROVIDER=mock` — **zero Gemini calls, no API key**. It requires the demo
  corpus seeded (`scripts/seed_demo.py`).
- Options: `--project-id`, `--dataset`, `--out-dir`, `--k 5 10`, `--legs ...`,
  `--no-ai`, `--baseline <previous evaluation-report.json>`.
- Outputs (gitignored `data/evaluation-output/`): `evaluation-report.json`
  (machine-readable) and `evaluation-report.md` (human-readable).
- Regression comparison: `--baseline` diffs each leg/K metric against the previous run
  and prints deltas (`+0.013` = improvement). Deltas are **informational** in Phase 7 —
  no threshold gates CI until a policy is justified by data.

### 4.1 Repro­ducibility

Every report records: `evaluationRunId`, `datasetVersion`, `timestamp`, project id, K
values, legs, AI-pipeline flag, embedding model + dimension, and AI model. Re-running
with the same inputs and the same corpus yields the same numbers (mock embeddings are
deterministic). No secrets are stored.

## 5. Analysis trace (observability)

Every analysis persists a per-stage trace (`analysis_runs.TraceJson`, schema
`trace-v1`):

- **Stages** with real wall-clock durations (`Context`, `AI Analysis`, `Persistence`
  for incident investigations; `Roslyn + Dependency Graph`, `AI Analysis`,
  `Persistence` for change risk). Stages the host cannot observe are not invented — the
  AI service's own latency is `usage.latencyMs`, and its per-item retrieval attribution
  is attached verbatim.
- **Retrieval trace** (`retrieval`): the queries run, `candidateCount` vs
  `selectedCount` (what was considered vs what entered the prompt), the budgets
  (`maxChunks`, `maxCharsPerChunk`), and each selected chunk with **per-leg
  attribution**: `vectorScore` (semantic similarity), `keywordRank` / `dependencyRank`
  (1-based positions). These are different signals and are **never comparable or
  summed** — the UI shows them separately.
- **Failure state**: normalized category (VALIDATION / AUTHORIZATION / RETRIEVAL /
  AI_PROVIDER / RATE_LIMIT / TIMEOUT / PERSISTENCE / INTERNAL) mapped from the
  persisted failure code.

Exposed via `GET /api/v1/analyses/{analysisId}/trace` (authorization identical to the
analysis: Read; non-members see 404). Raw prompts, tokens, JWTs and secrets are never
stored.

### 5.1 Tool calls (Phase 8, [docs/agent-tools.md](agent-tools.md))

The trace also records every tool call: `toolCallId`, `toolName`, status
(Proposed / Executed / Rejected / Failed), real duration, a truncated argument summary,
error code, and evidence-id count. These answer "why did the model fetch this evidence
and was it authorized?" without storing raw payloads.

### 5.2 Tool-loop metrics (measured by the runner, AI-service boundary)

The evaluation report's `summary.tools` block measures what the AI service can prove
with the deterministic mock provider — Python never executes tools, so authorization
and rejection are covered by .NET integration tests instead:

| Metric | Definition |
| --- | --- |
| `proposals` / `proposalsValid` | tool proposals made / whose name is in the allowlist catalog (proposal validity) |
| `loopCompleted` | cases where the deterministic loop reached a final result |
| `groundingAfterTools` | cases where the final result passed grounding after tool results were fed back |
| `toolsUsed` | distinct tools proposed (deterministic mock: `get_dependency_paths`, `get_runbook`) |

Phase 8 measured results (mock provider, synthetic corpus, 2026-08-15): 20/20 cases
loop-completed, 40/40 proposals valid, 20/20 grounded after tools. Tool authorization
success/rejection rates are not derivable in Python — they are asserted by integration
tests (unknown tool → `TOOL_NOT_ALLOWED`, cross-project → `NOT_FOUND`, max calls →
`TOOL_CALL_LIMIT_EXCEEDED`).

## 6. Error categories

| Category | Failure codes |
| --- | --- |
| VALIDATION | `AI_VALIDATION_FAILED`, `TOOL_CALL_LIMIT_EXCEEDED` (Phase 8) |
| RATE_LIMIT | `LLM_RATE_LIMITED` |
| TIMEOUT | `AI_TIMEOUT`, `JOB_TIMEOUT` |
| AI_PROVIDER | `AI_UNAVAILABLE` |
| INTERNAL | `QUEUE_FULL`, `WORKER_INTERRUPTED`, `INTERNAL` |

## 7. Why no LLM-as-judge (yet)

Deterministic evaluation is the Phase 7 design: it is free, reproducible, unbiased by
evaluator, and independent of the live Gemini schema issue. An LLM judge would add
cost, evaluator bias, and irreproducible variance without replacing the mechanical
checks above; it can be considered later as an *experimental* semantic metric,
explicitly labeled.

## 8. Limitations

- **Synthetic corpus + mock embeddings.** The measured numbers (§ of the final report
  of Phase 7) come from the AcmePay demo corpus with deterministic mock embeddings and
  the mock AI provider. They demonstrate the framework, not production accuracy —
  nothing here is claimed as production-grade.
- **Dependency leg** is change-model-driven; the golden queries are retrieval queries,
  so dependency-only recall is expected to be near zero on this dataset (reported
  honestly, and the change-risk path still exercises the leg end-to-end).
- Retrieval is evaluated at the document level; chunk-level precision is not annotated
  in v1.
- Remediation text is not semantically judged — only its structure and grounding.
- No per-leg retrieval traces are persisted per *production* analysis beyond the
  selected-evidence attribution (the full per-leg candidate lists are available in
  evaluation runs).
