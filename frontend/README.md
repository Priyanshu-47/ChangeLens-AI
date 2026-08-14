# frontend — React + TypeScript SPA

> **Phase 0 status: stub.** Scaffolded in Phase 5.

The product surface: dashboard, change analysis, incident investigation, interactive dependency graph, evidence inspection, AI evaluation dashboard, and AI run trace. Talks only to the ASP.NET Core API (`/api/v1`); never to the AI service or Gemini.

## Planned stack

- Vite + React + TypeScript (strict), React Router
- Tailwind + shadcn/ui component set
- Recharts (evaluation/timeline charts), React Flow (dependency graph)
- Typed API client generated from the backend's OpenAPI document
- Vitest + Testing Library

## Key references

- Screens & stories: [docs/mvp-scope.md](../docs/mvp-scope.md) (S1–S6)
- API contract: [docs/api-contract.md](../docs/api-contract.md)
- Async job polling: [docs/adr/0009-async-analysis-jobs.md](../docs/adr/0009-async-analysis-jobs.md)
