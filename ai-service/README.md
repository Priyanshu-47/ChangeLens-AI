# ai-service — Python FastAPI AI Service

> **Phase 2 — complete.** FastAPI + Gemini structured-output service: provider abstraction, versioned layered prompts, Pydantic validation with bounded repair and safe failure, grounding enforcement, correlation-id tracing, internal-key auth, mock provider for $0 dev/tests.

The AI capability provider of ChangeLens. It owns AI integration (providers, prompts, structured output, AI-specific validation) and — in later phases — ingestion, embeddings, retrieval, and evaluation. It **never** owns users, projects, incidents, authorization, or business state; the ASP.NET backend is the only client ([docs/ai-service-boundary.md](../docs/ai-service-boundary.md), [ADR-0002](../docs/adr/0002-service-boundary.md)).

## Architecture

```text
ASP.NET Core  ── POST /internal/v1/analysis/risk (X-Internal-Key, X-Correlation-ID) ──▶  FastAPI
                                                                                          │
                                                                                          ▼
                                                                            IAIProvider (Protocol)
                                                                              ├── GeminiProvider  (google-genai, structured outputs)
                                                                              └── MockAIProvider  (deterministic, AI_PROVIDER=mock)
```

The pipeline per analysis: layered prompt → provider call → Pydantic validation → deterministic post-checks (confidence bounds, array caps, **grounding rule**) → success, or bounded repair (max 2) → safe failure (`422 AI_VALIDATION_FAILED`) — unvalidated prose is never returned ([ADR-0007](../docs/adr/0007-structured-output-schema-validation.md)).

## Prerequisites

- Python **3.12+** (developed and verified on 3.14)
- A Gemini API key (free tier) for real LLM calls — **not required** for tests or mock mode

## Local setup

```bash
cd ai-service
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements-dev.txt   # Windows (use bin/ on POSIX)
cp .env.example .env           # fill in values (never commit .env)
```

Run with the **mock provider** (no API key, deterministic output):

```bash
AI_PROVIDER=mock INTERNAL_API_KEY=change-me-internal-key .venv/Scripts/python -m uvicorn app.main:app --port 8000
```

Run with **Gemini**:

```bash
AI_PROVIDER=gemini GEMINI_API_KEY=<your key> INTERNAL_API_KEY=change-me-internal-key \
  .venv/Scripts/python -m uvicorn app.main:app --port 8000
```

## Environment variables

All config is environment-driven (`pydantic-settings`, validated at startup — missing required values fail fast with a clear message). Full list in [`.env.example`](.env.example).

| Variable | Required | Default | Notes |
| --- | --- | --- | --- |
| `INTERNAL_API_KEY` | yes | — | Shared secret; the backend sends it as `X-Internal-Key`. Min 8 chars |
| `AI_PROVIDER` | no | `gemini` | `gemini` or `mock` (deterministic stand-in, no key needed) |
| `GEMINI_API_KEY` | when provider=gemini | — | Free tier; never committed |
| `GEMINI_TEXT_MODEL` | no | `gemini-3.7-flash` | Config, not code ([ADR-0005](../docs/adr/0005-llm-provider-abstraction.md)) |
| `GEMINI_TIMEOUT_SECONDS` | no | 60 | Provider call timeout |
| `GEMINI_MAX_RETRIES` | no | 3 | Retries only on 429/5xx, exponential backoff + jitter |
| `GEMINI_MAX_OUTPUT_TOKENS` | no | 8192 | Cost/latency bound |
| `GEMINI_INPUT_PRICE_PER_1M_USD` / `GEMINI_OUTPUT_PRICE_PER_1M_USD` | no | unset | When set, `estimatedCostUsd` is computed (labeled estimate); unset ⇒ `null`, never fabricated |
| `AI_MAX_REPAIR_ATTEMPTS` | no | 2 | Bounded structured-output repair |
| `AI_READINESS_PROBE` | no | `false` | `true` resolves the model name on `/ready` (metadata call). Off ⇒ health/readiness cost zero Gemini |
| `AI_MAX_EVIDENCE_CHARS` | no | 120000 | Token-budget trim for rendered evidence |
| `LOG_LEVEL` | no | `INFO` | |

## Endpoints

| Method | Path | Auth | Purpose |
| --- | --- | --- | --- |
| GET | `/health` | — | Liveness (process up, no Gemini) |
| GET | `/ready` | — | Readiness (config valid; probe only if `AI_READINESS_PROBE=true`) |
| GET | `/internal/v1/health/live` | internal key | Liveness (internal contract) |
| GET | `/internal/v1/health/ready` | internal key | Readiness (internal contract) |
| POST | `/internal/v1/analysis/risk` | internal key | Structured change-risk analysis over an evidence package |

Swagger/OpenAPI: `http://localhost:8000/docs` (public routes) — the internal routes require the `X-Internal-Key` + `X-Contract-Version: 1` headers.

All internal requests must carry `X-Contract-Version: 1` and (recommended) `X-Correlation-ID`; the service echoes/generates the correlation id and includes it in logs and error envelopes.

## Running the .NET → AI flow (mock)

With both services up and the backend configured with `Ai__BaseUrl=http://localhost:8000` and the same `INTERNAL_API_KEY`:

```bash
curl -s -X POST http://localhost:8000/internal/v1/analysis/risk \
  -H "X-Internal-Key: change-me-internal-key" -H "X-Contract-Version: 1" \
  -H "Content-Type: application/json" \
  -d '{"projectId":"p1","changeSummary":"Changed token refresh logic.","changedFiles":[{"path":"src/AuthClient.cs","changeType":"modified","language":"csharp"}]}'
```

Then from the backend: `POST /api/v1/analyses/change-risk` with a JWT (see [backend/README.md](../backend/README.md)).

## Tests

```bash
cd ai-service
.venv/Scripts/python -m pytest -q          # 57 tests — ZERO Gemini calls, no API key needed
```

Coverage: config validation, model validation (enums/bounds), grounding rule, prompt layering + injection sanitizer, bounded repair + safe failure, retry semantics, error mapping, HTTP contract (auth, correlation, envelopes), and the deterministic mock end-to-end.

Optional **live Gemini smoke test** (one minimal structured-output call — protects free-tier quota, off by default):

```bash
RUN_GEMINI_TESTS=true GEMINI_API_KEY=<your key> .venv/Scripts/python -m pytest tests/test_gemini_live.py -v -s
```

## Provider abstraction

`app/providers/base.py` defines the `IAIProvider` protocol; `GeminiProvider` is the MVP adapter and `MockAIProvider` the deterministic stand-in. The analysis service depends only on the protocol, so an `OpenAIProvider`/`BedrockProvider` can be added without touching orchestration or validation ([ADR-0005](../docs/adr/0005-llm-provider-abstraction.md)). The protocol currently declares only capabilities with consumers — `embed_texts` joins in Phase 3 when embeddings exist.

## Known limitations (Phase 2)

- **No RAG yet** (Phase 3): the analysis endpoint receives its evidence package directly in the request.
- **No persistence**: analysis runs / results are not stored (Phase 4 adds `analysis_runs` and result tables in the backend).
- **Incident investigation** (`/internal/v1/analysis/incident`) is Phase 4.
- **Costs**: token counts come from Gemini usage metadata when present; cost estimates only when per-model pricing env vars are configured.
- The `app` schema / DB is not used by this service yet — Phase 3 introduces the `ai` schema.

## Key references

- Boundary: [docs/ai-service-boundary.md](../docs/ai-service-boundary.md)
- LLM design: [docs/llm-integration.md](../docs/llm-integration.md)
- Decisions: [ADR-0002](../docs/adr/0002-service-boundary.md), [ADR-0005](../docs/adr/0005-llm-provider-abstraction.md), [ADR-0007](../docs/adr/0007-structured-output-schema-validation.md)
