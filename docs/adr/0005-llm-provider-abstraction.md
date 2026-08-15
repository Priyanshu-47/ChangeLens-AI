# ADR-0005: LLM provider abstraction, Gemini first

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

The brief requires an `IAIProvider`-style seam so OpenAI/Bedrock can replace Gemini without rewriting the application, and prohibits hardcoding deprecated models. Gemini is the initial provider (free tier, structured outputs via `responseSchema`).

## Decision

The AI service defines `IAIProvider` (Protocol) with `complete_structured`, `complete_text`, `embed_texts`, implemented by `GeminiProvider` (MVP). Orchestration, retrieval, prompts, and persistence depend only on the protocol and on Pydantic models — never on provider SDK types. Provider-specific features (e.g. `responseSchema`, safety settings) are normalized inside the adapter.

Model names are configuration (`GEMINI_TEXT_MODEL`, `GEMINI_EMBEDDING_MODEL` in `.env.example`), validated at startup/readiness against the API so a deprecated model fails fast. The default is a current GA model (gemini-3.7-flash, checked Aug 2026); the eval gate (Phase 8) detects quality regressions if the model is swapped.

## Consequences

- Gemini→OpenAI/Bedrock is a new adapter + config change, not an application rewrite.
- We record model + prompt version per run, so results are reproducible and provider swaps are auditable.
- Cost: adapter normalization work; some provider features (grounding, tools) need per-adapter mapping; we deliberately don't use provider-exclusive features outside the adapter.
