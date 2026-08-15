# Cost Model

> Phase 9 deliverable. Honest numbers, not promises. ChangeLens is **designed to run locally at zero infrastructure cost**; live AI features depend on the Gemini free tier, which is rate-limited and not guaranteed forever.

## Local development (the official MVP)

| Item | Cost | Notes |
| --- | --- | --- |
| PostgreSQL + pgvector (Docker container) | **$0** | `docker compose up`; data in the `pgdata` named volume |
| .NET SDK / Node / Python | **$0** | already-installed developer tooling |
| Gemini API (text + embeddings) | **$0 on the free tier** | rate-limited (e.g. ~20 text requests/day for the configured model); quota-gated live tests only |
| Mock providers (`AI_PROVIDER=mock`, `EMBEDDING_PROVIDER=mock`) | **$0** | deterministic; used by tests, CI, and the evaluation runner — **zero Gemini calls** |
| GitHub repo + Actions (public) | **$0** | CI runs mocks only; no Gemini calls, no AWS |

**Total local MVP: $0.**

## CI

All CI jobs use mock providers and a local PostgreSQL service container:

- backend build + unit + integration tests
- Python unit + DB integration tests
- frontend build + tests
- deterministic evaluation runner (seeded demo corpus, mock embeddings)

No Gemini key is present in CI; live Gemini tests are gated by `RUN_GEMINI_TESTS=true` and never run in CI. **CI completes at $0.**

## Public demo hosting (optional, post-MVP)

The primary demo is local Docker. Free-tier hosting is a *documented option*, never a promise — ephemeral free tiers (Render/Railway free plans, static SPA hosting) vary in cold-start behavior, persistence, and expiry. Any hosted demo must be clearly labeled as a demo, not production. See [docs/deployment-strategy.md](deployment-strategy.md) §3 for the trade-offs.

## AWS (future, optional — never provisioned in the MVP)

A future AWS path is documented (CloudFront + S3 static, Fargate backend + AI service, RDS/Aurora PostgreSQL) with cost estimates produced **before** any resource is created, and only with explicit approval. Nothing in the MVP requires AWS. See [docs/deployment-strategy.md](deployment-strategy.md) §4.

## What is NOT claimed

- Not "free forever" — the Gemini free tier can change; a hosted deployment may incur real costs.
- No fabricated token counts, latency, or spend figures anywhere in the project (the AI service only reports usage actually returned by the SDK; optional per-model pricing env vars are unset by default).
