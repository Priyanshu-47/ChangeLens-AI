# docker — Containerization & local orchestration

> **Phase 1 status:** a root `docker-compose.yml` provides PostgreSQL for local development. Dockerfiles and the four-service stack (frontend, backend, ai-service, postgres) land in Phase 9.

## Current layout

```
docker-compose.yml         root compose — postgres service (Phase 1)
                          ← expanded to the full stack in Phase 9
docker/README.md           this strategy note
```

> Deviation from the Phase 0 plan (`docker/compose.postgres.yml`): the postgres service was placed in a **root `docker-compose.yml`** so that `docker compose up` works as documented, and Phase 9 grows the same file into the four-service stack. `scripts/start-local-postgres.sh` covers machines without Docker.

## Strategy (target, Phase 9)

`docker compose up` runs the full product locally, $0:

| Service | Image | Notes |
| --- | --- | --- |
| `postgres` | `pgvector/pgvector:pg18` | Single instance, `app` + `ai` schemas; named volume `pgdata`; healthcheck gates dependents |
| `backend` | multi-stage .NET 10 SDK → runtime | Non-root; only client of the AI service |
| `ai-service` | python slim + requirements | Internal network only; optional local-embedding layer |
| `frontend` | dev: Vite (HMR) / prod: nginx | `VITE_API_BASE_URL` from env |

Principles: healthchecks before dependencies, non-root containers, secrets only via env (never image layers), named volumes for data, internal network for service-to-service traffic.

## Without Docker

`bash scripts/start-local-postgres.sh` runs a project-local PostgreSQL instance (port 5433, trust auth, data in `pgdata/local-dev`) using already-installed PostgreSQL binaries. Stop it with `bash scripts/stop-local-postgres.sh`.

## Key references

- [docs/deployment-strategy.md](../docs/deployment-strategy.md)
- [docs/development-sequence.md](../docs/development-sequence.md) (Phases 1, 9)
- [docs/adr/0003-single-postgres-schema-per-service.md](../docs/adr/0003-single-postgres-schema-per-service.md)
