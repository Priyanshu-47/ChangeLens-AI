# ChangeLens AI — Screenshots

The final screenshot set for the portfolio is **six images**, all captured from the
**real running application** (Docker stack, `http://localhost:8080`, React production
build) using a headless Chrome browser against the live UI. No mockups, no
placeholders, no generated images.

All data shown is the **synthetic AcmePay demo dataset** (demo incident "HTTP 401
after JWT signing-key rotation", deterministic mock AI provider). It is not
production data.

| Screenshot | Purpose |
| --- | --- |
| `01-dashboard.png` | Project dashboard — AcmePay context, incident/analysis counts, recent incidents & analyses |
| `02-incident-detail.png` | Incident detail — JWT signing-key rotation incident, severity, service, timeline |
| `03-investigation-result.png` | Completed investigation — root-cause candidates, confidence, evidence, remediation, unknowns |
| `04-analysis-trace.png` | Analysis trace — per-stage timings, tool calls, retrieval explorer (desktop viewport) |
| `05-tool-trace.png` | Tool calls — `get_dependency_paths` + `get_runbook`, execution status and durations |
| `06-change-risk.png` | Change-risk result — risk level, confidence, impacted components, evidence, validation |

## Provenance

- Application: `http://localhost:8080` (docker compose stack, four healthy services)
- Login: seeded demo account `engineer@changelens.dev` / `EngineerPass!2026` (development-only)
- Project: `AcmePay` (synthetic)
- Data: real API responses from the running backend with the **mock AI provider** (zero Gemini calls)
- Tooling: headless Chrome via `puppeteer-core`; `04-analysis-trace.png` captured at a 2048×1280 desktop viewport (3072×1920 px @1.5x)

## Re-capturing

```bash
docker compose up -d --build
ai-service/.venv/Scripts/python scripts/acmepay_demo.py   # recreate the canonical demo data
# then log in at http://localhost:8080 with a demo account and capture manually,
# or re-run the puppeteer capture script used to produce these images.
```
