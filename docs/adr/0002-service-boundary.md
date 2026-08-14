# ADR-0002: ASP.NET Core orchestrates; FastAPI is a capability provider

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

Two reasonable homes exist for workflow orchestration (change risk, incident investigation): the .NET backend (which owns auth, domain, persistence, audit) or the Python service (which owns retrieval and LLM work). Putting orchestration in Python would pull business entities, authorization, and audit into the AI service — duplicating the backend's role and making the AI layer hard to replace.

## Decision

The ASP.NET Core API orchestrates both workflows: it performs deterministic preprocessing (Roslyn parsing, dependency computation, contract extraction, incident normalization), assembles the evidence package, calls the AI service for retrieval and for a single structured reasoning step, validates results, persists them, and audits everything. The FastAPI service exposes narrow capability endpoints (`ingest`, `search`, `analysis/risk`, `analysis/incident`, `evaluations/run`) and holds no business truth, user auth, or workflow state.

## Consequences

- Auth, authorization, audit, and domain invariants exist in exactly one place.
- The AI service is stateless and swappable; Gemini→OpenAI changes touch only the AI service (ADR-0005).
- Cost: every analysis makes bounded network round trips between services; the internal API is a contract that must be versioned and tested (contract tests in CI).
- Tool use (Phase 6) follows the same boundary: AI proposes, .NET executes (ADR-0008).
