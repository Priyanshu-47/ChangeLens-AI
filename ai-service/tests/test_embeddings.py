"""Mock embedding provider (brief §41) — deterministic, zero Gemini calls."""

import pytest

from app.embeddings import MockEmbeddingProvider
from app.embeddings.base import EmbeddingDimensionError, EmbeddingResult


def test_mock_embedding_is_deterministic():
    provider = MockEmbeddingProvider(dimension=64)
    a1 = provider.embed_texts(["hello world"]).vectors[0]
    a2 = provider.embed_texts(["hello world"]).vectors[0]
    assert a1 == a2


def test_mock_embedding_dimension_matches_config():
    provider = MockEmbeddingProvider(dimension=128)
    assert len(provider.embed_texts(["x"]).vectors[0]) == 128


def test_mock_embedding_batch():
    provider = MockEmbeddingProvider(dimension=32)
    result = provider.embed_texts(["one", "two", "three"])
    assert isinstance(result, EmbeddingResult)
    assert len(result.vectors) == 3
    assert result.model == provider.model
    assert result.model_version == provider.model_version
    assert result.dimension == 32


def test_mock_model_version_encodes_dimension():
    provider = MockEmbeddingProvider(dimension=768, model="mock-gemini-embedding-2")
    assert provider.model_version == "mock-gemini-embedding-2@768d"


def test_mock_model_tracks_configured_real_model():
    # build_embedding_provider derives the mock label from the configured model, so
    # changing GEMINI_EMBEDDING_MODEL changes the mock version (→ re-embed triggers).
    from app.embeddings import build_embedding_provider
    from app.config import Settings

    settings = Settings(
        internal_api_key="test-internal-key",
        ai_provider="mock",
        embedding_provider="mock",
        gemini_embedding_model="gemini-embedding-2",
        embedding_dimension=768,
    )
    provider = build_embedding_provider(settings)
    assert provider.model == "mock-gemini-embedding-2"
    assert provider.model_version == "mock-gemini-embedding-2@768d"


def test_similar_texts_produce_similar_vectors():
    provider = MockEmbeddingProvider(dimension=64)
    base = provider.embed_texts(["retry the payment gateway request"]).vectors[0]
    similar = provider.embed_texts(["retry the payment gateway request now"]).vectors[0]
    unrelated = provider.embed_texts(["database migration failed"]).vectors[0]

    def cosine(a, b):
        return sum(x * y for x, y in zip(a, b))

    assert cosine(base, similar) > cosine(base, unrelated)


def test_mock_never_raises_dimension_errors():
    # Mock always returns its configured dimension; dimension mismatch is a
    # provider-contract concern handled by the ingestion service.
    provider = MockEmbeddingProvider(dimension=16)
    assert provider.dimension == 16


@pytest.mark.parametrize("dimension", [0, -5, 1.5, "768"])
def test_provider_rejects_invalid_dimension(dimension):
    with pytest.raises(ValueError):
        MockEmbeddingProvider(dimension=dimension).embed_texts(["x"])
