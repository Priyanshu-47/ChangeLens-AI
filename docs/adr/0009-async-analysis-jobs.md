# ADR-0009: Long AI analyses run as async jobs (202 + poll)

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

An analysis takes seconds to tens of seconds (retrieval + LLM with possible repair + tool turns). Synchronous request/response would exhaust HTTP timeouts, block the UI, and make retries/observability awkward. The brief's API sketch already implies job resources (`GET /api/analyses/{id}`).

## Decision

Operations that invoke the AI pipeline (`analyze`, `investigate`, `ingest`, `evaluations/run`) return **202 Accepted** with `Location` pointing at a job resource. Jobs follow `Queued → Running → Succeeded | Failed` with structured error codes (`AI_VALIDATION_FAILED`, `LLM_RATE_LIMITED`, `RETRIEVAL_UNAVAILABLE`, …). The frontend polls with capped backoff (no websockets in MVP). Idempotency: client `requestId` prevents duplicate LLM spend on retries.

Job state and AI-run metadata live in the `app` schema (`analysis_runs`, `evaluation_runs`) — a database-backed job queue with a bounded worker, sufficient at portfolio scale; no Redis/Kafka needed (ADR-0003).

## Consequences

- Production-shaped UX and failure handling; jobs are observable and retryable.
- The job runner is a natural place for per-analysis instrumentation (tokens, latency, cost) feeding the trace and evaluation views.
- Cost: polling API surface + job state machine; the in-DB queue has a throughput ceiling — irrelevant for MVP, documented as a Phase 10 note (SQS if ever needed).
