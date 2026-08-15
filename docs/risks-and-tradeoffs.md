# Risks & Technical Trade-offs

> Phase 0 deliverable. What could go wrong, what we gave up, and what still needs a decision.

## 1. Risk register

| # | Risk | L | I | Mitigation | Owner phase |
| --- | --- | --- | --- | --- | --- |
| R1 | Gemini model deprecation / availability changes break analyses | M | H | Model names are config, not code; readiness probe fails fast; `.env.example` defaults reviewed at each phase; eval gate catches quality regressions | 2, 7 |
| R2 | Structured-output validation failures (schema drift, weak model) | M | H | Native `responseSchema` + Pydantic + bounded repair + safe failure; schema versioning; schema-validation-failure rate is an eval metric | 2, 7 |
| R3 | LLM hallucination in root-cause candidates | M | H | Grounding rule (evidence ids required), confidence + status(Candidate), unknowns field, evidence panel in UI; hallucination rate measured in eval | 4, 7 |
| R4 | Prompt injection via repo contents/incidents | M | H | Layered prompt architecture (primary), deterministic pre-scan (depth), grounding makes injected "facts" visible | 2, 4 |
| R5 | Free-tier rate limits / latency cause flaky demos | H | M | Backoff + retry, `LLM_RATE_LIMITED` job status, local-embedding mode, mocked LLM in tests, demo seeded to minimize LLM calls | 2, 4 |
| R6 | Two runtimes (C# + Python) double the ops surface | M | M | Contracts pinned in Phase 0, single repo, contract tests in CI, AI service kept stateless | 1, 2, 9 |
| R7 | Shared-DB coupling (app/ai schemas) creates migration conflicts | M | M | Strict schema ownership + separate migration tools; cross-schema access only via API; CI runs both migration sets against fresh DB | 1, 3 |
| R8 | Roslyn-only deep parsing; TS/Python chunking is best-effort | M | M | C# is the first-class demo language; others are retrieval-only; documented as a known limitation | 3, 4 |
| R9 | Embedding model change invalidates vectors | M | M | Model-versioned embeddings + re-index workflow + hash-based idempotent ingest | 3 |
| R10 | Golden dataset too small / biased → eval numbers misleading | M | M | Honest labeling (dataset size, slice limits), metrics scoped to the dataset, "measured on N cases" in UI | 7 |
| R11 | Free-tier hosting demo flakiness (cold starts, expiry) | H | L | Local Docker is the primary demo; free tiers are optional and caveated | 9 |
| R12 | Solo-developer bandwidth across three codebases | H | M | Vertical slices per phase; contracts before code; tests prevent regressions | all |
| R13 | Roslyn graph is best-effort semantics (unresolved references, top-level statements, exotic syntax) → missing edges | M | M | Semantic model over syntax trees + documented extraction scope; edge types limited to what Roslyn proves; unknown edges surface as warnings, never as claims; deterministic fixture tests pin the supported shapes | 4 |
| R14 | Local-git change source misused (traversal, weird revisions, repo escaping sandbox) | L | H | Path/revision validation, fixed argument list (no shell), repository restricted to configured root, project isolation regression tests; analyzed source is parsed, never executed | 4 |
| R15 | In-memory dependency graph rebuilt per analysis → latency / no cross-analysis reuse | M | L | Demo-scale graph builds in ~1–2 s; deterministic change identifiers allow future caching in Postgres; measured durations recorded, no fabricated perf claims | 4 |
| R16 | Dependency retrieval leg ranks by connectivity, so top hits may not match the change text | M | M | Dependency is an explicit third RRF list, never blended into vector scores; per-leg metadata explains *why* each hit surfaced; Phase 7 evaluation compares modes (dependency-only recall is expected near zero on retrieval-style golden queries — reported honestly) | 4 |

L = likelihood (L/M/H), I = impact (L/M/H).

## 2. Trade-off log (decisions and what they cost)

| Decision | What we get | What we gave up / pay | Ref |
| --- | --- | --- | --- |
| Python FastAPI AI service vs .NET-only | Best tooling for chunking/embeddings; replaceable AI layer | Second runtime, network boundary, Python dependency surface | ADR-0002 |
| .NET orchestrates everything | Auth/domain/audit in one place; AI service stays dumb & replaceable | Cross-service round trips on every analysis | ADR-0002 |
| One Postgres instance, two schemas | $0, one backup, one container | No independent scaling; migration discipline required | ADR-0003 |
| pgvector instead of managed vector DB | Free, relational + vector co-located | Scale ceiling (fine for portfolio); HNSW tuning is on us | ADR-0003/4 |
| Hybrid retrieval (vector+keyword+RRF) | Robust matches, eval-comparable legs | More moving parts than vector-only | ADR-0004 |
| Own RAG pipeline, no LangChain | Explicit, testable, evaluable; prompt control | We write chunkers/merge logic; slower feature velocity | architecture §8 |
| Single controlled tool-using agent | Auditable, bounded, honest | No flashy "multi-agent" (deliberate) | ADR-0008 |
| Async 202+job pattern | Production-shaped UX, retryable, observable | Polling complexity, job state machine | ADR-0009 |
| Identity+JWT vs Cognito in MVP | Simple, local, testable | Real SSO is a later seam | ADR-0012 |
| Local embeddings + mocked LLM in CI/tests | $0 test budget, deterministic tests | CI doesn't exercise real Gemini (smoke tests do, manually) | ADR-0006, llm §6 |
| C# first-class parsing; others best-effort | Depth where it matters for the demo | Multi-language depth is post-MVP | R8 |
| Roslyn in .NET, retrieval in AI service (ADR-0011) | Symbol/dependency analysis where the ecosystem is strongest; stable evidence ids across the boundary | Two runtimes must agree on the change model contract | ADR-0011 |
| In-memory dependency graph instead of Neo4j | One database, no extra infra, rebuilt per analysis | No persistent graph queries or cross-run graph analytics | R15 |
| Local git change source instead of GitHub integration | Safe, deterministic, demo-controlled; no webhooks | Real-PR workflows (webhooks, remote fetch) are post-MVP | R14 |
| Dependency as a separate retrieval leg (RRF) instead of blended scores | Connectivity evidence stays explainable and comparable | Ranking across heterogeneous legs needs per-leg metadata | R16 |

## 3. Open questions for review

1. **Reranker in MVP?** Default: no (RRF only). If the demo shows weak ranking on runbooks, add a local cross-encoder before a future eval pass. Confirm.
2. **Hosting demo appetite?** Local-Docker-only MVP, or should Phase 9 also stand up a free-tier URL (accepting cold starts/expiry) for the README? Cost stays $0 either way.
3. **Demo repository choice:** a small public .NET sample repo (e.g. a stripped `eShop`-style or custom demo service) seeded in `data/` — confirm before Phase 3 seeds it.
4. **Golden dataset size:** 15–25 cases proposed (S M). Any preference for a specific incident archetype mix (deploy regression, config drift, auth, data migration)?
5. **.NET identity + JWT vs. minimal hand-rolled JWT:** Identity chosen (real-world credibility); confirm it's acceptable for portfolio size.
6. **PostgreSQL minor version:** `pgvector/pgvector` pinned to PostgreSQL 17 (LTS-ish, broadly tested) — 18 is fine if preferred.

## 4. What would change these decisions

- A hard requirement for multi-language deep analysis ⇒ push tree-sitter symbol analysis into Phase 3 scope (larger).
- A requirement to demo with real GitHub PRs ⇒ add a GitHub App webhook + git-diff fetch phase (post-MVP).
- Measured retrieval quality below target in Phase 7 ⇒ add reranker + query rewriting before shipping the eval dashboard (metrics are synthetic-corpus/mock-embedding numbers, not production claims).
