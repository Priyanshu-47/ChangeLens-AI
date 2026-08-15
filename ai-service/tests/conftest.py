"""Shared test fixtures.

The default test app uses the deterministic MockAIProvider — the normal test suite
performs ZERO Gemini calls and does not require a GEMINI_API_KEY.
"""

from __future__ import annotations

import os
import sys

# Ensure `import app` works when pytest is run from the repo root as well.
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Safe env defaults so importing app.main (which builds the ASGI app from env) is harmless.
os.environ.setdefault("INTERNAL_API_KEY", "test-internal-key")
os.environ.setdefault("AI_PROVIDER", "mock")
os.environ.setdefault("EMBEDDING_PROVIDER", "mock")
os.environ.setdefault("GEMINI_TEXT_MODEL", "gemini-3.1-flash-lite")

import pytest  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

from app.config import Settings  # noqa: E402
from app.main import create_app  # noqa: E402

TEST_INTERNAL_KEY = "test-internal-key"


def make_settings(**overrides) -> Settings:
    base = {
        "internal_api_key": TEST_INTERNAL_KEY,
        "ai_provider": "mock",
        "embedding_provider": "mock",
        # Unit tests never touch the database: retrieval must stay OFF unless an
        # integration test explicitly turns it on with a real pgvector database.
        "ai_auto_retrieve": False,
        "ai_max_repair_attempts": 2,
    }
    base.update(overrides)
    return Settings(**base)


@pytest.fixture
def settings():
    return make_settings()


@pytest.fixture
def app(settings):
    return create_app(settings)


@pytest.fixture
def client(app):
    with TestClient(app) as c:
        yield c


def auth_headers(**extra) -> dict[str, str]:
    headers = {
        "X-Internal-Key": TEST_INTERNAL_KEY,
        "X-Contract-Version": "1",
    }
    headers.update(extra)
    return headers
