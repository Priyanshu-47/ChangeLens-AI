# Project Description — ChangeLens AI

> Phase 10 deliverable. Short, 100-word, GitHub, and resume versions. All claims are implemented, tested, and measured in this repository.

## 2-line version

ChangeLens AI is a full-stack AI platform that answers "what could this code change break?" and "what actually broke, and what evidence supports it?" — via Roslyn-based change intelligence, hybrid RAG, grounded structured output, and a controlled tool loop.

## 100-word version

ChangeLens AI is an AI-powered production change-risk and incident-intelligence platform built with React, ASP.NET Core 10, FastAPI, and PostgreSQL + pgvector. Workflow A analyzes code changes with Roslyn and a dependency graph, retrieves evidence via hybrid RAG (pgvector + full-text + dependency leg, fused with RRF), and produces schema-validated, evidence-grounded risk reports. Workflow B runs async incident investigations: 202 + polling, a normalized incident context, a bounded tool loop where the AI proposes and .NET validates/authorizes/executes/audits, and grounded root-cause candidates with explicit unknowns. A deterministic 20-case golden-dataset evaluation, per-analysis traces, project isolation, and a $0-first CI (zero Gemini spend) make the results trustworthy and inspectable.

## GitHub version (repo description + README intro)

**ChangeLens AI — AI-powered production change risk & incident intelligence platform.**

ChangeLens helps engineering teams answer two questions around every production change: *before deployment*, "what could this break?" — and *after deployment*, "what changed, what is affected, and what evidence supports the root cause?". It combines Roslyn source analysis, dependency-graph impact analysis, hybrid RAG over historical incidents/runbooks/code, structured LLM reasoning, a controlled tool loop, async incident investigation, deterministic evaluation, and full AI traceability. Two services: ASP.NET Core 10 (domain, authz, orchestration, audit, tool execution) and FastAPI (providers, prompts, structured output, retrieval). $0-first: local Docker + PostgreSQL + mock providers; CI runs all suites with zero Gemini calls.

## Resume version

See [resume-bullets.md](resume-bullets.md) — three bullets, compact summary, and a detailed project section with measured results.
