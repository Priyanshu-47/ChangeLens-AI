# Agent tools (Phase 8): AI proposes, the application executes

- **Decision:** [ADR-0008](adr/0008-controlled-tool-use.md) — single-agent loop, tools executed in .NET.
- **Implemented:** Phase 8 (commit that lands this document).

## Why a single agent

The brief explicitly warns against multi-agent architectures as orchestration theater.
ChangeLens uses **one AI reasoning loop**: the application owns orchestration, the model
proposes, and the application decides. There is no planner/researcher/analyst split,
no agent-to-agent messaging — just a bounded loop over a typed, allowlisted tool set.

## The tool loop

```
AI turn
  ├─ no proposal ──► final structured response (grounded, validated)
  └─ tool proposal ─► registry lookup → argument validation → project authorization
                      → execution (bounded timeout) → sanitized result
                      ──► fed back as DATA ──► next AI turn
```

- Bounded: `Analysis:MaxToolCalls` (default 3, `AI_MAX_TOOL_CALLS`). Exceeding the
  limit fails the analysis with `TOOL_CALL_LIMIT_EXCEEDED` — never an infinite loop.
- Each tool call has its own timeout (`Analysis:ToolTimeoutSeconds`, default 30).
- Read-only tools are idempotent, so a transient AI failure that restarts the loop
  from turn one is safe to replay.

## Ownership boundary (unchanged from ADR-0002/0008)

| Layer | Owns |
| --- | --- |
| .NET (Application) | allowlist, argument validation, project authorization, execution, audit, trace, loop orchestration |
| Python (AI service) | prompts, tool catalog rendering, proposal parsing (`kind: tool_call | final`), response validation/grounding |

Python **never executes tools**. The catalog is data; proposals are parsed and
rendered; tool results are untrusted input to the prompt builder.

## The tool registry (allowlist)

Only tools registered in `ToolRegistry` (explicit DI wiring — no dynamic discovery)
can be proposed. Unknown names are rejected with `TOOL_NOT_ALLOWED` and fed back to
the model as a safe structured error. All Phase 8 tools are **LOW risk, read-only**;
the policy layer supports Medium/High (with approval) later without redesign.

| Tool | Input | Data source | Project-isolated |
| --- | --- | --- | --- |
| `get_incident` | incidentId (uuid) | .NET DB (Incident) | ✅ (cross-project → NOT_FOUND) |
| `get_incident_timeline` | incidentId, limit | .NET DB (IncidentEvent, chronological) | ✅ |
| `get_service` | serviceId (uuid) | .NET DB (Service) | ✅ |
| `get_runbook` | query, topK ≤ 5 | AI-service retrieval (Runbook docs) | ✅ (project injected) |
| `get_source_symbol` | symbol (identifier) | AI-service retrieval (SourceCode) | ✅ |
| `get_dependency_paths` | symbol, maxDepth ≤ 4 | Roslyn dependency graph (demo repo) | ✅ (in-memory graph only) |
| `search_evidence` | query, documentType?, topK ≤ 10 | AI-service hybrid retrieval | ✅ |

Deliberately **not** implemented (brief §41–44): raw SQL execution, shell/process
execution, arbitrary URL fetching, and any write tool (no create/update/delete/deploy).

## Safety properties

- **Project isolation (§7):** the project id comes from the analysis context, never
  from AI-supplied arguments. Cross-project lookups resolve to `NOT_FOUND` (no
  existence leak).
- **Argument validation (§8):** wrong types, invalid UUIDs, empty identifiers, and
  out-of-range values are rejected as `INVALID_ARGUMENT` before execution.
- **Identifier safety (§10–11):** `get_source_symbol` / `get_dependency_paths` accept
  identifiers only — `..`, drive letters, URI schemes, and shell metacharacters are
  rejected; `maxDepth` is bounded.
- **Prompt injection defense (§15):** tool results are rendered inside the DATA stream
  (`<tool_results>`), pre-scanned by the same instruction-stripper as evidence, and the
  tool prompt explicitly forbids following instructions inside tool results. The tool
  layer remains authoritative: a runbook cannot enable a disabled tool, change
  authorization, or alter project scope.
- **Sanitization (§14):** outputs are structured JSON (capped at 60 KB), with
  `evidenceIds` attached by the executor. The grounding validator admits **only** those
  ids — ids appearing inside narrative text are not citable.

## Grounding after tools

Tool outputs become evidence. Every final root-cause candidate must still reference
>= 1 id in the (tool-extended) evidence index; the mechanical grounding validator
remains the authority. A tool proposal itself carries no claims, so it is not
"grounded" — only final results are.

## Trace, audit, observability

- **Trace:** every call is recorded (`analysis_runs.TraceJson.toolCalls`) with
  toolCallId, tool name, status (Proposed/Executed/Rejected/Failed), real duration,
  a truncated argument summary, error code, and evidence-id count. Exposed via
  `GET /api/v1/analyses/{id}/trace` (same authorization as the analysis).
- **Audit:** `ToolExecuted` / `ToolRejected` entries per call (analysisId, tool,
  status, duration, failure code) — never secrets or raw payloads.
- **Logs:** structured per-call events with analysisId, tool, status, duration.

## Configuration

```env
# backend appsettings / environment
Analysis__MaxToolCalls=3
Analysis__ToolTimeoutSeconds=30
```

## Evaluation

The evaluation runner measures what the AI service can prove (docs/evaluation.md §5.2):

- proposal validity (proposed name ∈ catalog)
- deterministic loop completion (mock turns reach a final result)
- grounding of the final result after tool results were fed back

Tool **authorization** and **rejection** are .NET behaviors covered by integration
tests (cross-project isolation, unknown-tool rejection, max-call limit, timeout),
not by the Python runner.

## Known limitations

- The deterministic `MockAIProvider` always proposes `get_dependency_paths` then
  `get_runbook` — a scripted plan for tests, not an emergent capability.
- Real Gemini tool proposals are blocked by the open `gemini-3.1-flash-lite`
  structured-output schema issue; live tool use is not claimed until that resolves.
- No human-approval UI: LOW-risk read-only tools execute without approval by design
  (the policy layer supports it later).
