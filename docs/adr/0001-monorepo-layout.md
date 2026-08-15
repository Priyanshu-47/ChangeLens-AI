# ADR-0001: Monorepo with three deployable units

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

The product spans three runtimes (React SPA, ASP.NET Core API, Python AI service) that must evolve in lockstep: the .NET ↔ Python contract, API DTOs, prompt versions, and schemas all change together. The project is built by one developer and is a portfolio artifact.

## Decision

Use a single Git repository containing `frontend/`, `backend/`, `ai-service/`, `docker/`, `docs/`, and `data/`. Each directory is independently deployable, but the repo is versioned as one unit. CI (Phase 10) builds and tests each unit and gates on the cross-service contract tests.

## Consequences

- Contract changes (API or internal) ship as one atomic change; cross-service drift is caught in CI.
- Single PR review, single history, simpler branching for a solo developer.
- Cost: repo is bigger; deployment of one unit requires releasing the repo state it's pinned to (managed with per-unit tags/images later if ever needed).
- Cross-language tooling (one lockfile per unit, not one for everything) — acceptable.
