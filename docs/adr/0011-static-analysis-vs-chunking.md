# ADR-0011: Roslyn in .NET for analysis; tree-sitter in the AI service for chunking

- **Status:** Accepted
- **Date:** 2026-08-14

## Context

Source code is used twice: (a) deep, deterministic analysis — symbols, dependencies, API contracts, changed-method impact — which is the core of Workflow A; and (b) chunking for semantic retrieval. One parser could serve both; a single Python-based parser (tree-sitter) is weaker for C# semantics, while a single .NET parser can't cover JS/TS/Python for retrieval chunking.

## Decision

Two tools, two purposes, deliberately:

- **Analysis (backend, .NET):** Roslyn for C# — syntax + semantic models give exact class/method boundaries, dependency edges (references, calls, inheritance), and changed-API detection. This is deterministic code, never an LLM (brief §14).
- **Chunking for retrieval (AI service, Python):** tree-sitter with language grammars (C#, JS/TS, Python) produces coarse semantic boundaries (file → class → method/function) for embedding. Same hierarchy concept as Roslyn, but purpose-built for chunk content.

C# is the **first-class demo language** for deep analysis; other languages get best-effort chunking and retrieval but not full symbol-level analysis in MVP (documented limitation).

## Consequences

- Deep, trustworthy change analysis where it matters (demo: .NET service) and retrieval coverage across languages.
- Cost: two parsers to maintain; chunk boundaries may differ slightly from Roslyn symbols (acceptable — chunking is for retrieval, Roslyn for analysis; evidence linking uses file paths + line ranges to stay consistent).
