"""Seed the `ai` schema with the ChangeLens demo corpus.

Usage (from ai-service/):

    DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens" \
    EMBEDDING_PROVIDER=mock INTERNAL_API_KEY=dev-key ./.venv/Scripts/python scripts/seed_demo.py

Default EMBEDDING_PROVIDER is "mock" (deterministic vectors, zero cost, no Gemini key).
Set EMBEDDING_PROVIDER=gemini and GEMINI_API_KEY to embed with the real model.

The script is idempotent: re-running with unchanged files reports SKIPPED and
makes no Gemini calls (docs/rag-architecture.md §4).
"""

from __future__ import annotations

import argparse
import logging
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.config import Settings  # noqa: E402
from app.db import Database, create_engine_for  # noqa: E402
from app.embeddings import build_embedding_provider  # noqa: E402
from app.ingestion.service import IngestionService  # noqa: E402
from app.models.requests import IngestDocumentItem, IngestDocumentsRequest  # noqa: E402

logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s %(message)s")
logger = logging.getLogger("seed_demo")


def load_repository(repo_dir: Path, project_id: str) -> list[IngestDocumentItem]:
    """Walk the demo C# repository; each .cs file becomes a SourceCode document.

    The document id is deterministic (relative path) so re-seeding is idempotent.
    """
    items: list[IngestDocumentItem] = []
    ignored_dirs = {"bin", "obj", ".git", ".vs"}
    for path in sorted(repo_dir.rglob("*.cs")):
        if any(part in ignored_dirs for part in path.parts):
            continue
        rel = path.relative_to(repo_dir).as_posix()
        items.append(
            IngestDocumentItem(
                id=f"{project_id}:code:{rel}",
                document_type="SourceCode",
                repository_id="acmepay",
                service_id="acmepay-api",
                file_path=rel,
                language="csharp",
                environment="production",
                title=rel,
                content=path.read_text(encoding="utf-8"),
            )
        )
    return items


def load_markdown_corpus(corpus_dir: Path, project_id: str, document_type: str) -> list[IngestDocumentItem]:
    items: list[IngestDocumentItem] = []
    for path in sorted(corpus_dir.glob("*.md")):
        rel = path.name
        title = path.read_text(encoding="utf-8").splitlines()[0].lstrip("# ").strip()
        items.append(
            IngestDocumentItem(
                id=f"{project_id}:{document_type.lower()}:{path.stem}",
                document_type=document_type,
                repository_id="acmepay",
                service_id="acmepay-api",
                file_path=rel,
                language="markdown",
                environment="production",
                title=title,
                content=path.read_text(encoding="utf-8"),
            )
        )
    return items


def main() -> None:
    parser = argparse.ArgumentParser(description="Seed the ChangeLens ai schema with demo data")
    parser.add_argument("--project-id", default="demo-project", help="project_id to ingest under")
    parser.add_argument("--reindex", action="store_true", help="force re-embedding of unchanged chunks")
    parser.add_argument("--repository", type=Path, default=PROJECT_ROOT / "data" / "demo-repository")
    parser.add_argument("--incidents", type=Path, default=PROJECT_ROOT / "data" / "demo-incidents")
    parser.add_argument("--runbooks", type=Path, default=PROJECT_ROOT / "data" / "demo-runbooks")
    args = parser.parse_args()

    settings = Settings()
    db = Database(create_engine_for(settings))
    embedding = build_embedding_provider(settings)
    service = IngestionService(db=db, embedding=embedding, settings=settings)

    documents = (
        load_repository(args.repository, args.project_id)
        + load_markdown_corpus(args.incidents, args.project_id, "Incident")
        + load_markdown_corpus(args.runbooks, args.project_id, "Runbook")
    )

    request = IngestDocumentsRequest(
        project_id=args.project_id,
        documents=documents,
        reindex=args.reindex,
    )

    logger.info(
        "seeding %d documents (provider=%s model=%s dim=%d project=%s)",
        len(documents), settings.embedding_provider, embedding.model, embedding.dimension,
        args.project_id,
    )
    result = service.ingest(request)

    print(f"\n=== Seed summary (project={args.project_id}) ===")
    print(f"documents ingested: {len(result.document_ids)}")
    print(f"chunks created:     {result.chunk_count}")
    print(f"skipped (idempotent): {result.skipped}")
    if result.errors:
        print(f"errors:             {len(result.errors)}")
        for err in result.errors[:10]:
            print(f"  - {err}")
    else:
        print("errors:             0")


if __name__ == "__main__":
    main()
