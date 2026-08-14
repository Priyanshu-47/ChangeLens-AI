"""Real-PostgreSQL integration tests (pgvector required).

These tests are gated: they run only when TEST_DATABASE_URL points at a PostgreSQL
database with pgvector installed (and the `ai` schema migrated). The normal unit
suite never touches a database and never makes Gemini calls.

    TEST_DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens_test" \
    ./.venv/Scripts/python -m pytest tests/test_db_integration.py -q
"""

from __future__ import annotations

import os

import pytest

from app.config import Settings
from app.db import Database, create_engine_for
from app.embeddings import MockEmbeddingProvider
from app.ingestion.service import IngestionService
from app.models.requests import (
    IngestDocumentItem,
    IngestDocumentsRequest,
    RetrievalSearchRequest,
)
from app.retrieval.service import RetrievalService

TEST_DATABASE_URL = os.environ.get("TEST_DATABASE_URL")

pytestmark = pytest.mark.skipif(
    not TEST_DATABASE_URL,
    reason="TEST_DATABASE_URL not set; skipping PostgreSQL integration tests",
)


def make_settings(**overrides) -> Settings:
    base = {
        "internal_api_key": "test-internal-key",
        "ai_provider": "mock",
        "embedding_provider": "mock",
        "ai_auto_retrieve": False,
        "database_url": TEST_DATABASE_URL,
    }
    base.update(overrides)
    return Settings(**base)


@pytest.fixture(scope="module")
def services():
    settings = make_settings()
    db = Database(create_engine_for(settings))
    embedding = MockEmbeddingProvider(dimension=settings.embedding_dimension)
    ingestion = IngestionService(db=db, embedding=embedding, settings=settings)
    retrieval = RetrievalService(db=db, embedding=embedding, settings=settings)

    # Isolated test project namespace.
    project = "test-project"
    with db.session() as session:
        session.execute(__import__("sqlalchemy").text(
            f"DELETE FROM ai.documents WHERE project_id = '{project}'"
        ))
    return db, embedding, ingestion, retrieval, project


def _doc(project: str, doc_id: str, content: str, **extra) -> IngestDocumentItem:
    base = {
        "id": f"{project}:{doc_id}",
        "document_type": "SourceCode",
        "language": "csharp",
        "file_path": f"{doc_id}.cs",
        "title": doc_id,
        "content": content,
    }
    base.update(extra)
    return IngestDocumentItem(**base)


def _ingest(ingestion, project: str, docs: list[IngestDocumentItem]) -> dict:
    result = ingestion.ingest(IngestDocumentsRequest(project_id=project, documents=docs))
    return result.to_dict()


# --- idempotency & content change ---


def test_ingest_is_idempotent(services):
    _, _, ingestion, _, project = services
    content = "public class IdempotentThing {\n    public void Do() { }\n}"
    doc = _doc(project, "idem", content)

    first = _ingest(ingestion, project, [doc])
    second = _ingest(ingestion, project, [doc])

    assert first["chunkCount"] >= 1
    assert second["chunkCount"] == 0
    assert second["skipped"] == 1

    with services[0].session() as session:
        from sqlalchemy import func, select
        from app.models.ai import AiDocumentChunk

        count = session.execute(
            select(func.count(AiDocumentChunk.id)).where(AiDocumentChunk.document_id == doc.id)
        ).scalar_one()
        assert count == first["chunkCount"]


def test_content_change_rechunks_and_reembeds(services):
    _, _, ingestion, _, project = services
    doc_id = f"{project}:changed"
    first = _ingest(ingestion, project, [_doc(project, "changed", "public class Old { }")])
    changed = _ingest(
        ingestion,
        project,
        [_doc(project, "changed", "public class New {\n    public void Added() { }\n}")],
    )

    assert changed["chunkCount"] >= 1
    # Old chunks were replaced, not duplicated.
    with services[0].session() as session:
        from sqlalchemy import func, select
        from app.models.ai import AiDocumentChunk

        count = session.execute(
            select(func.count(AiDocumentChunk.id)).where(AiDocumentChunk.document_id == doc_id)
        ).scalar_one()
        assert count == changed["chunkCount"]


# --- retrieval legs ---


def test_keyword_retrieval_finds_exact_terms(services):
    _, _, ingestion, retrieval, project = services
    _ingest(
        ingestion,
        project,
        [
            _doc(project, "gateway", "public class TimeoutException handler with HttpClient retry logic"),
            _doc(project, "auth", "JWT token validation and signing key rotation"),
        ],
    )

    response = retrieval.search(
        RetrievalSearchRequest(
            project_id=project, query="TimeoutException HttpClient retry", strategy="keyword", k=5
        )
    )
    ids = {r.document_id for r in response.results}
    assert f"{project}:gateway" in ids


def test_vector_retrieval_prefers_similar(services):
    _, _, ingestion, retrieval, project = services
    _ingest(
        ingestion,
        project,
        [
            _doc(
                project, "vs_a",
                "public class GatewayRetry { public void Run() { retry the payment gateway request with backoff } }",
            ),
            _doc(
                project, "vs_b",
                "public class Unrelated { public void Run() { database migration schema table } }",
            ),
        ],
    )

    response = retrieval.search(
        RetrievalSearchRequest(
            project_id=project, query="retry the payment gateway request", strategy="vector", k=3
        )
    )
    assert response.results
    assert response.results[0].document_id == f"{project}:vs_a"


def test_metadata_filter_excludes_other_service(services):
    _, _, ingestion, retrieval, project = services
    _ingest(
        ingestion,
        project,
        [
            _doc(project, "svc_a", "public class A { public void Run() { retry payment gateway } }", service_id="payments"),
            _doc(project, "svc_b", "public class B { public void Run() { retry payment gateway } }", service_id="ledger"),
        ],
    )

    response = retrieval.search(
        RetrievalSearchRequest(
            project_id=project,
            query="retry payment gateway",
            strategy="keyword",
            k=5,
            filters={"serviceId": "ledger"},
        )
    )
    assert response.results
    assert all(r.metadata["service"] == "ledger" for r in response.results)


def test_project_isolation_is_enforced(services):
    _, _, ingestion, retrieval, project = services
    _ingest(
        ingestion,
        project,
        [_doc(project, "secret", "public class SecretCredentials { JWT signing key and api key }")],
    )

    response = retrieval.search(
        RetrievalSearchRequest(
            project_id="another-project", query="JWT signing key api key", strategy="hybrid", k=10
        )
    )
    assert response.results == []


def test_hybrid_retrieval_ranks_relevant_above_irrelevant(services):
    _, _, ingestion, retrieval, project = services
    _ingest(
        ingestion,
        project,
        [
            _doc(project, "hy_rel", "public class RetryHandler { public void Run() { retry TimeoutException with HttpClient } }"),
            _doc(project, "hy_irr", "public class ThemePark { public void Run() { roller coaster ticket refund } }"),
        ],
    )

    response = retrieval.search(
        RetrievalSearchRequest(
            project_id=project, query="retry TimeoutException HttpClient", strategy="hybrid", k=5
        )
    )
    assert response.results
    assert response.results[0].document_id == f"{project}:hy_rel"


def test_document_type_filter(services):
    _, _, ingestion, retrieval, project = services
    _ingest(
        ingestion,
        project,
        [
            _doc(project, "code", "public class C { public void Run() { retry payment } }"),
            IngestDocumentItem(
                id=f"{project}:inc",
                document_type="Incident",
                language="markdown",
                file_path="inc.md",
                title="inc",
                content="# INC: retry payment\n\n## Symptom\n\nGateway retries failed.",
            ),
        ],
    )

    response = retrieval.search(
        RetrievalSearchRequest(
            project_id=project, query="retry payment", strategy="keyword", k=5,
            document_types=["Incident"],
        )
    )
    assert response.results
    assert all(r.document_type == "Incident" for r in response.results)
