# Screenshots

No screenshots were captured for this release.

The Phase 10 environment has no browser/browser-automation tooling, so the UI could not be opened and screenshotted. Per the project's no-fabrication rule, no mockups or generated images are published here.

What was verified instead (and where):

- **API end-to-end:** the canonical demo was driven against the real running stack (login → project → incident → investigate `202` → poll `Queued → Running → Succeeded` → grounded root-cause candidates → trace with real stage timings + tool calls → change-risk with `riskLevel=MEDIUM`, validation `valid`). See the Phase 10 final report and [docs/demo-script.md](../demo-script.md).
- **UI behavior:** 34 React component tests cover every screen in the demo journey (login, dashboard, incidents, incident detail + timeline, investigate + 202, async polling, analysis result with evidence linking, grounding badge, unknowns, trace + retrieval explorer + tool calls, change-risk submission and result).

## How to capture the real screenshots

On any machine with Docker + a browser:

```bash
cp .env.example .env
docker compose up -d --build
cd ai-service && DATABASE_URL="postgresql+psycopg://changelens:changelens_dev_password@localhost:5432/changelens" \
  EMBEDDING_PROVIDER=mock ./.venv/Scripts/python scripts/seed_demo.py
# open http://localhost:8080, log in with engineer@changelens.dev / EngineerPass!2026,
# walk the journey in docs/demo-script.md, then save:
#   01-login.png 02-dashboard.png 03-incidents.png 04-incident-detail.png
#   05-investigation-running.png 06-investigation-result.png 07-evidence-trace.png
#   08-tool-trace.png 09-change-risk.png 10-evaluation.png
```
