# docker — Containerization & local orchestration

> **Phase 9 status:** the full four-service stack (frontend, backend, ai-service, postgres) is defined in the root `docker-compose.yml`, with multi-stage Dockerfiles for backend and ai-service, and a new frontend container (React build → `nginx-unprivileged`). Docker itself is **not available in the authoring environment** — the compose file and Dockerfiles are statically validated; run `docker compose up --build` on a Docker-equipped machine as the Phase 9 exit check.

## Current layout

```
docker-compose.yml         root compose — full stack: postgres + ai-service + backend + frontend
backend/Dockerfile         multi-stage .NET 10 SDK → non-root runtime
ai-service/Dockerfile      python slim, non-root, alembic upgrade + uvicorn
frontend/Dockerfile        node build → nginx-unprivileged (SPA + /api proxy)
frontend/nginx.conf        SPA fallback, hashed-asset caching, /api → backend:5000
backend/.dockerignore      build-context hygiene
frontend/.dockerignore     build-context hygiene
docker/README.md           this strategy note
```

> Deviation from the Phase 0 plan (`docker/compose.postgres.yml`): the postgres service lives in the **root `docker-compose.yml`** so `docker compose up` works as documented. `scripts/start-local-postgres.sh` covers machines without Docker.

## The stack (Phase 9)

`docker compose up --build` runs the full product locally, $0:

| Service | Image | Notes |
| --- | --- | --- |
| `postgres` | `pgvector/pgvector:pg18` | Single instance, `app` + `ai` schemas; named volume `pgdata`; healthcheck gates dependents |
| `backend` | multi-stage .NET 10 SDK → runtime | Non-root; only client of the AI service; DB-gated healthcheck `/api/v1/health` |
| `ai-service` | python slim + requirements | Internal network only; host port exposed for local dev only (remove `ports:` for shared deployments) |
| `frontend` | React build → nginx-unprivileged (non-root, :8080) | Same-origin: nginx serves the SPA and proxies `/api/` to the backend — no CORS in production; `VITE_API_BASE_URL` baked at build time (browser-visible, never secrets) |

Principles: healthchecks before dependencies, non-root containers, secrets only via env (never image layers), named volumes for data, internal network for service-to-service traffic.

## Without Docker

`bash scripts/start-local-postgres.sh` runs a project-local PostgreSQL instance (port 5433, trust auth, data in `pgdata/local-dev`) using already-installed PostgreSQL binaries. Stop it with `bash scripts/stop-local-postgres.sh`.

## Quick start

```bash
cp .env.example .env        # fill INTERNAL_API_KEY + JWT_SIGNING_KEY
docker compose up -d --build
cd ai-service && DATABASE_URL="postgresql+psycopg://changelens:changelens_dev_password@localhost:5432/changelens" \
  EMBEDDING_PROVIDER=mock ./.venv/Scripts/python scripts/seed_demo.py
# open http://localhost:8080 — demo script: docs/demo-script.md
```

See [docs/deployment-strategy.md](../docs/deployment-strategy.md) for the production topology and the dev-only port exposures.

## Key references

- [docs/deployment-strategy.md](../docs/deployment-strategy.md)
- [docs/development-sequence.md](../docs/development-sequence.md) (Phases 1, 9)
- [docs/costs.md](../docs/costs.md)
- [docs/adr/0003-single-postgres-schema-per-service.md](../docs/adr/0003-single-postgres-schema-per-service.md)
