# ai-service — Python FastAPI AI Service

> **Phase 0 status: stub.** Scaffolded in Phase 2.

The AI capability provider: document ingestion, semantic chunking, embeddings, hybrid retrieval, structured LLM reasoning, controlled tool-call proposals, and evaluation. Owns the `ai` schema (documents, chunks, embeddings) and talks to Gemini through a provider abstraction.

## Planned structure

```
app/
├── api/          internal REST endpoints (/internal/v1/*)
├── chunking/     structure-aware chunkers (code, markdown, incidents, OpenAPI)
├── embeddings/   provider abstraction (gemini / local sentence-transformers)
├── retrieval/    hybrid search (vector + keyword + metadata + RRF), rerankers
├── llm/          IAIProvider + GeminiProvider, versioned prompts, structured output + repair
├── evaluation/   golden dataset runner + metrics
└── core/         pydantic-settings config, db (ai schema), schemas, observability
tests/            unit, retrieval, schema-validation, prompt regression, evaluation
```

## Key references

- Boundary: [docs/ai-service-boundary.md](../docs/ai-service-boundary.md)
- RAG: [docs/rag-architecture.md](../docs/rag-architecture.md)
- LLM: [docs/llm-integration.md](../docs/llm-integration.md)
- Decisions: [docs/adr/0002-service-boundary.md](../docs/adr/0002-service-boundary.md), [0005-llm-provider-abstraction.md](../docs/adr/0005-llm-provider-abstraction.md), [0006-embedding-provider-abstraction.md](../docs/adr/0006-embedding-provider-abstraction.md), [0007-structured-output-schema-validation.md](../docs/adr/0007-structured-output-schema-validation.md)
