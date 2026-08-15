# ADR-0008: Controlled single-agent tool use, executed in .NET

- **Status:** Accepted (implemented in Phase 8 — see [docs/agent-tools.md](../agent-tools.md))
- **Date:** 2026-08-14

## Context

The brief wants selective tool-using AI with validation, authorization, timeouts, retries, and audit — and explicitly warns against "multi-agent" as marketing. Tools operate on data the AI service doesn't own (incidents, deployments, logs, dependency graph), which live behind .NET's auth and audit.

## Decision

A **single agent loop**, not a multi-agent system. The AI service proposes tool calls (e.g. `search_incidents`, `get_deployment`, `get_logs`); the backend owns tool schemas, validates inputs, authorizes per project, executes with timeout + retry limits, appends results to the conversation, and audit-logs every call (proposed, executed, rejected — with reason). Each call round-trips through the internal API; `analysis_runs.tool_calls` records the full sequence with outcomes and latency.

Tools are used only when retrieval/evidence is insufficient — the workflow favors a single structured reasoning call; tool use is a bounded enhancement (max N calls per analysis, configurable).

## Consequences

- Tools never execute without backend authorization; audit + trace make every AI action explainable.
- The design is honest: one agent, controlled loop, no orchestration theater.
- Cost: multi-turn latency and token spend are bounded by the call cap; tool schemas must be versioned with the contract.

## Implementation notes (Phase 8)

- Seven LOW-risk read-only tools ship: `get_incident`, `get_incident_timeline`, `get_service`, `get_runbook`, `get_source_symbol`, `get_dependency_paths`, `search_evidence` — all project-isolated, all executed in .NET (docs/agent-tools.md).
- The loop (`ToolLoopOrchestrator`) bounds calls (`Analysis:MaxToolCalls`, default 3) and timeouts (`Analysis:ToolTimeoutSeconds`, default 30); exceeding the limit fails the run with `TOOL_CALL_LIMIT_EXCEEDED`.
- No shell/SQL/URL/write tools were introduced; grounding remains authoritative after tool use.
- The Python AI service parses proposals and renders tool results as untrusted DATA; it never executes tools.
