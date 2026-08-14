"""Alembic environment — migrates the `ai` schema only.

Before the first migration it ensures the pgvector extension and the `ai` schema
exist (both idempotent). The extension is REQUIRED: if pgvector is unavailable the
migration fails with a clear error instead of producing a broken schema.
"""

from __future__ import annotations

import logging
import os

from alembic import context
from sqlalchemy import create_engine, text

from app.models.ai import Base

config = context.config
target_metadata = Base.metadata

# The ai schema owns the alembic version table (strict ownership, ADR-0003).
VERSION_TABLE = "alembic_version_ai"
VERSION_TABLE_SCHEMA = "ai"

# Migrations only need the database URL — no app secrets (INTERNAL_API_KEY etc.).
DEFAULT_DATABASE_URL = "postgresql+psycopg://changelens@127.0.0.1:5433/changelens"
DATABASE_URL = os.environ.get("DATABASE_URL") or DEFAULT_DATABASE_URL

logging.getLogger("alembic").setLevel(logging.INFO)


def _bootstrap(connection) -> None:
    connection.execute(text("CREATE EXTENSION IF NOT EXISTS vector"))
    connection.execute(text("CREATE SCHEMA IF NOT EXISTS ai"))
    connection.commit()


def run_migrations_offline() -> None:
    context.configure(
        url=DATABASE_URL,
        target_metadata=target_metadata,
        literal_binds=True,
        include_schemas=True,
        include_object=include_object,
        version_table=VERSION_TABLE,
        version_table_schema=VERSION_TABLE_SCHEMA,
        compare_type=True,
    )
    with context.begin_transaction():
        context.run_migrations()


def include_object(object_, name, type_, reflected, compare_to) -> bool:
    """The ai schema owns everything here — never touch the app schema (ADR-0003)."""
    if type_ == "table":
        return object_.schema == "ai"
    return True  # indexes / constraints follow their table


def run_migrations_online() -> None:
    connectable = create_engine(DATABASE_URL)
    with connectable.connect() as connection:
        _bootstrap(connection)
        context.configure(
            connection=connection,
            target_metadata=target_metadata,
            include_schemas=True,
            include_object=include_object,
            version_table=VERSION_TABLE,
            version_table_schema=VERSION_TABLE_SCHEMA,
            compare_type=True,
        )
        with context.begin_transaction():
            context.run_migrations()
    connectable.dispose()


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
