"""SQLAlchemy engine + session helpers for the `ai` schema (ADR-0003).

The AI service owns ONLY the `ai` schema (documents, chunks, embeddings). It never
touches the `app` schema owned by the .NET backend. The engine is created lazily —
no connection happens at import time, so unit tests never need a database.
"""

from __future__ import annotations

from contextlib import contextmanager

from sqlalchemy import create_engine
from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session, sessionmaker


def create_engine_for(settings) -> Engine:
    return create_engine(
        settings.database_url,
        echo=settings.database_echo,
        pool_pre_ping=True,
    )


class Database:
    """Holds the engine and hands out short-lived sessions."""

    def __init__(self, engine: Engine):
        self._engine = engine
        self._session_factory = sessionmaker(bind=engine, expire_on_commit=False)

    @property
    def engine(self) -> Engine:
        return self._engine

    @contextmanager
    def session(self) -> Session:
        session = self._session_factory()
        try:
            yield session
            session.commit()
        except Exception:
            session.rollback()
            raise
        finally:
            session.close()
