"""Hybrid retrieval (docs/rag-architecture.md §5, ai-service-boundary.md §3).

Legs: pgvector cosine similarity (semantic) + PostgreSQL full-text on the 'simple'
configuration (exact technical terms: TimeoutException, 401, JWT, migration — no
stemming). Metadata filters are applied INSIDE each SQL leg, and project_id is a
hard server-side filter on every query — a request can never see another project's
documents, regardless of what the caller passes.
"""

from __future__ import annotations

import logging
import re
import time

from sqlalchemy import text

from ..config import Settings
from ..db import Database
from ..embeddings.base import (
    EmbeddingAuthError,
    EmbeddingError,
    EmbeddingRateLimited,
    EmbeddingUnavailable,
    IEmbeddingProvider,
)
from ..errors import AiProviderError, AiRateLimitedError, AiRequestError
from ..models.requests import RetrievalSearchRequest, SearchFilters
from ..models.responses import (
    RetrievalResultItem,
    RetrievalResultSources,
    RetrievalSearchResponse,
    RetrievalUsage,
)
from .rrf import reciprocal_rank_fusion

logger = logging.getLogger(__name__)


class RetrievalService:
    def __init__(self, *, db: Database, embedding: IEmbeddingProvider, settings: Settings):
        self._db = db
        self._embedding = embedding
        self._settings = settings

    def search(self, request: RetrievalSearchRequest) -> RetrievalSearchResponse:
        started = time.perf_counter()

        if request.embedding_model is not None and request.embedding_model != self._embedding.model:
            raise AiRequestError(
                f"Embedding model override {request.embedding_model!r} is not supported yet; "
                f"configured model is {self._embedding.model!r}."
            )

        query = request.query.strip()
        if not query:
            raise AiRequestError("query must not be empty.")

        strategy = request.strategy
        candidate_k = self._settings.retrieval_candidate_k

        vector_scores: dict[str, float] = {}
        keyword_scores: dict[str, float] = {}
        embedding_tokens: int | None = None

        if strategy in ("vector", "hybrid"):
            vector_ranking, vector_scores, embedding_tokens = self._vector_leg(query, request, candidate_k)
        else:
            vector_ranking = []
        if strategy in ("keyword", "hybrid"):
            keyword_ranking, keyword_scores = self._keyword_leg(query, request, candidate_k)
        else:
            keyword_ranking = []

        if strategy == "hybrid":
            fused = reciprocal_rank_fusion([vector_ranking, keyword_ranking], k=self._settings.rrf_k)
            ranked = fused[: request.k]
        elif strategy == "vector":
            ranked = [(item_id, vector_scores[item_id]) for item_id in vector_ranking][: request.k]
        else:
            ranked = [(item_id, keyword_scores[item_id]) for item_id in keyword_ranking][: request.k]

        results = self._hydrate(request.project_id, ranked, strategy, vector_scores, keyword_ranking)

        latency_ms = int((time.perf_counter() - started) * 1000)
        logger.info(
            "retrieval_completed",
            extra={
                "strategy": strategy,
                "results": len(results),
                "latencyMs": latency_ms,
                "vectorCandidates": len(vector_ranking),
                "keywordCandidates": len(keyword_ranking),
            },
        )
        return RetrievalSearchResponse(
            results=results,
            usage=RetrievalUsage(
                queries=[query],
                latency_ms=latency_ms,
                tokens={"embedding": embedding_tokens} if embedding_tokens is not None else {},
                strategy=strategy,
            ),
        )

    def search_queries(self, project_id: str, queries: list[str], *, k: int | None = None) -> list[RetrievalResultItem]:
        """Run several queries and merge results by chunk id (first hit wins), capped at k.

        Used by the analysis flow to build an evidence package from the change summary
        plus changed-file names — deterministic order, no duplicates.
        """
        limit = k or self._settings.retrieval_top_k
        merged: dict[str, RetrievalResultItem] = {}
        for query in queries:
            if not query.strip():
                continue
            response = self.search(RetrievalSearchRequest(project_id=project_id, query=query, k=limit))
            for item in response.results:
                merged.setdefault(item.chunk_id, item)
        return list(merged.values())[:limit]

    # --- legs ---

    def _vector_leg(self, query: str, request: RetrievalSearchRequest, candidate_k: int):
        try:
            embedded = self._embedding.embed_texts([query])
        except EmbeddingRateLimited as exc:
            raise AiRateLimitedError("Embedding provider rate limited; try again shortly.") from exc
        except (EmbeddingUnavailable, EmbeddingAuthError) as exc:
            raise AiProviderError("Embedding provider is temporarily unavailable.") from exc
        except EmbeddingError as exc:
            raise AiProviderError(f"Embedding failed: {exc}") from exc

        vector_text = "[" + ",".join(repr(v) for v in embedded.vectors[0]) + "]"
        sql = text(
            """
            SELECT e.chunk_id AS chunk_id,
                   (e.vector <=> CAST(:vector_text AS vector)) AS distance
            FROM ai.embeddings e
            JOIN ai.document_chunks c ON c.id = e.chunk_id
            JOIN ai.documents d ON d.id = c.document_id
            WHERE c.project_id = :project_id
              AND e.model_version = :model_version
              {filter_clauses}
            ORDER BY e.vector <=> CAST(:vector_text AS vector)
            LIMIT :limit
            """.format(filter_clauses=self._filter_sql(request))
        )
        params = self._base_params(request, candidate_k)
        params["vector_text"] = vector_text
        params["model_version"] = self._embedding.model_version

        rows = []
        with self._db.session() as session:
            rows = session.execute(sql, params).mappings().all()

        ranking: list[str] = []
        scores: dict[str, float] = {}
        for row in rows:
            chunk_id = str(row["chunk_id"])
            similarity = max(0.0, 1.0 - float(row["distance"]))
            ranking.append(chunk_id)
            scores[chunk_id] = similarity
        return ranking, scores, embedded.input_tokens

    def _keyword_leg(self, query: str, request: RetrievalSearchRequest, candidate_k: int):
        # OR'd terms (not plainto_tsquery's implicit AND): a natural-language query
        # never contains only terms that all co-occur in one chunk. Exact technical
        # terms survive because 'simple' config does no stemming (TimeoutException,
        # JWT, 401, retry, migration match verbatim).
        terms = [t for t in re.split(r"[^\w]+", query.lower()) if len(t) >= 2]
        tsq = " | ".join(terms) if terms else "''"
        sql = text(
            """
            SELECT c.id AS chunk_id,
                   ts_rank(c.content_tsv, to_tsquery('simple', :query)) AS rank
            FROM ai.document_chunks c
            JOIN ai.documents d ON d.id = c.document_id
            WHERE c.project_id = :project_id
              AND c.content_tsv @@ to_tsquery('simple', :query)
              {filter_clauses}
            ORDER BY rank DESC, c.id
            LIMIT :limit
            """.format(filter_clauses=self._filter_sql(request))
        )
        params = self._base_params(request, candidate_k)
        params["query"] = tsq

        rows = []
        with self._db.session() as session:
            rows = session.execute(sql, params).mappings().all()

        ranking: list[str] = []
        scores: dict[str, float] = {}
        for row in rows:
            chunk_id = str(row["chunk_id"])
            ranking.append(chunk_id)
            scores[chunk_id] = float(row["rank"])
        return ranking, scores

    # --- shared bits ---

    def _base_params(self, request: RetrievalSearchRequest, limit: int) -> dict:
        params: dict = {"project_id": request.project_id, "limit": limit}
        if request.document_types:
            params["document_types"] = request.document_types
        f = request.filters or SearchFilters()
        if f.service_id:
            params["service_id"] = f.service_id
        if f.language:
            params["language"] = f.language
        if f.environment:
            params["environment"] = f.environment
        return params

    @staticmethod
    def _filter_sql(request: RetrievalSearchRequest) -> str:
        clauses: list[str] = []
        if request.document_types:
            clauses.append("d.document_type = ANY(:document_types)")
        f = request.filters or SearchFilters()
        if f.service_id:
            clauses.append("c.service = :service_id")
        if f.language:
            clauses.append("c.language = :language")
        if f.environment:
            clauses.append("c.environment = :environment")
        return "\n".join(f"AND {clause}" for clause in clauses)

    def _hydrate(
        self,
        project_id: str,
        ranked: list[tuple[str, float]],
        strategy: str,
        vector_scores: dict[str, float],
        keyword_ranking: list[str],
    ) -> list[RetrievalResultItem]:
        if not ranked:
            return []
        ids = [item_id for item_id, _ in ranked]
        keyword_rank_map = {item_id: i + 1 for i, item_id in enumerate(keyword_ranking)}

        sql = text(
            """
            SELECT c.id AS chunk_id, c.chunk_type, c.symbol, c.content, c.metadata,
                   c.path, c.language, c.service, c.incident_id, c.environment,
                   d.id AS document_id, d.document_type, d.title
            FROM ai.document_chunks c
            JOIN ai.documents d ON d.id = c.document_id
            WHERE c.project_id = :project_id AND c.id = ANY(:ids)
            """
        )
        rows = []
        with self._db.session() as session:
            rows = session.execute(sql, {"project_id": project_id, "ids": ids}).mappings().all()

        by_id = {str(r["chunk_id"]): r for r in rows}
        results: list[RetrievalResultItem] = []
        for item_id, final_score in ranked:
            row = by_id.get(item_id)
            if row is None:
                continue
            vector_score = vector_scores.get(item_id) if strategy in ("vector", "hybrid") else None
            keyword_rank = keyword_rank_map.get(item_id) if strategy in ("keyword", "hybrid") else None
            results.append(
                RetrievalResultItem(
                    chunk_id=item_id,
                    document_id=row["document_id"],
                    document_type=row["document_type"],
                    chunk_type=row["chunk_type"],
                    source=row["path"],
                    content=row["content"],
                    metadata={
                        "title": row["title"],
                        "path": row["path"],
                        "language": row["language"],
                        "service": row["service"],
                        "incidentId": row["incident_id"],
                        "environment": row["environment"],
                        "symbol": row["symbol"],
                        "chunkMetadata": row["metadata"] or {},
                    },
                    score=round(final_score, 6),
                    sources=RetrievalResultSources(
                        vector=round(vector_score, 6) if vector_score is not None else None,
                        keyword=keyword_rank,
                    ),
                )
            )
        return results
