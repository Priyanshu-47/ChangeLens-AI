# ChangeLens AI — Frontend (React + TypeScript + Vite)

The investigation dashboard for ChangeLens. Implements both core workflows:

- **Workflow A — Change Risk:** submit a change, get a grounded risk report (risk level,
  changed/impacted symbols, dependency paths, risk factors, evidence).
- **Workflow B — Incident Investigation:** open an incident, submit an async investigation
  (202 → poll), and explore evidence-linked root-cause candidates, remediation, and unknowns.

The frontend talks **only** to the ASP.NET Core API (`/api/v1`). It never calls the AI service
or Gemini directly — there are no AI credentials in the browser.

## Prerequisites

- Node.js ≥ 18 (developed against Node 22)
- The backend running on `http://localhost:5000` (see `backend/README.md`)
  - The backend must be running with its worker (`AnalysisWorker`) enabled so investigations
    progress past `Queued`.

## Environment variables

Copy `.env.example` to `.env.local` (optional — defaults work for local dev):

```sh
# Base URL of the ASP.NET Core API (public config — browser-visible).
# NEVER put Gemini keys, JWT secrets, or credentials in VITE_* variables:
# Vite inlines them into the client bundle, where they are readable by anyone.
VITE_API_BASE_URL=http://localhost:5000/api/v1
```

## Development

```sh
npm install
npm run dev        # http://localhost:5173
```

Demo login (dev seed): `engineer@changelens.dev` / `EngineerPass!2026`
(also `viewer@…`/`ViewerPass!2026` and `admin@…`/`AdminPass!2026` — see the seed data).

## Build

```sh
npm run build      # tsc --noEmit && vite build → dist/
npm run preview    # serve the production build locally
```

## Tests

```sh
npm test           # vitest run (jsdom, mocked HTTP — zero Gemini calls)
npm run test:watch
```

The test suite mocks `fetch` at the API boundary. Coverage includes login + protected routes,
project selection, incident list/detail, 202 investigation submission, async polling
(Queued → Running → Succeeded, stop-on-terminal, stop-on-error), succeeded/failed analysis
rendering, evidence linking (root cause → evidence ids), grounding display, unknowns, and the
change-risk report.

## Project structure

```
src/
  api/        typed client + DTO mirrors of the backend contract (client.ts, endpoints.ts, types.ts)
  auth/       AuthContext + ProtectedRoute (JWT in localStorage, session restore via /auth/me)
  projects/   ProjectContext (selected project = UI context; backend stays authoritative)
  hooks/      useAsync, useAnalysisPolling
  components/ Layout (sidebar/topbar), ui primitives, Timeline, Investigation
  pages/      Login, Dashboard, Incidents, IncidentDetail, Analyses, Analysis, ChangeRisk
  styles/     global.css (design system)
```

## Design principles

- **No fabricated metrics** — every number comes from the API (counts, confidence, latency).
- **Evidence vs analysis** — the UI visually separates retrieved evidence from AI inference;
  every root-cause candidate links to the evidence ids it cites.
- **Grounding is backend-computed** — the UI displays `validationStatus` as-is; it never
  recalculates or overrides it.
- **Honest status** — the analysis page shows `Queued`/`Running`/`Succeeded`/`Failed` exactly
  as the backend reports; it does not invent internal stages.
- **No secrets in the browser** — no Gemini key, no credentials; errors render safe messages
  and codes only (never stack traces).
