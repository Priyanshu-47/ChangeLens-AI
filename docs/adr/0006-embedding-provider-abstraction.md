# ADR-0006: Embedding provider abstraction (Gemini + local)

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

Embeddings are the foundation of retrieval, but the project must stay $0-first and testable offline: unit tests and CI must not consume the Gemini API for every test run. The brief also requires re-indexing when the embedding model changes.

## Decision

`EmbeddingProvider` interface with two implementations: `GeminiEmbeddingProvider` (default for the demo, model configurable, free tier) and `LocalEmbeddingProvider` (`sentence-transformers`, e.g. `all-MiniLM-L6-v2`) for offline dev, unit tests, and CI. Provider is selected by `EMBEDDING_PROVIDER` env.

Vectors are stored **model-versioned** in `ai.embeddings(chunk_id, model, version, vector)`. Re-indexing (model change) is an explicit workflow: re-embed documents whose hash is unchanged, insert new-version vectors, flip the active version; old vectors remain for cross-model evaluation comparisons.

## Consequences

- CI and unit tests run fully offline and at $0; the demo uses Gemini embeddings.
- Changing embedding models is a supported operation with a UI signal ("re-index required") rather than a silent corruption.
- Cost: dimension differences between models require migrations (vector column per model/dims); a small local model is weaker than Gemini for retrieval — acceptable for tests, and the eval framework can quantify the gap.
