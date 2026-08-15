"""Deterministic mock embedding provider (brief §41).

Produces unit-norm-ish pseudo-vectors derived from a SHA-256 of the text via a
deterministic PRNG — no random module state, fully repeatable. Designed so that
similar strings produce similar vectors (character n-gram overlap drives the hash
input), giving vector search *some* lexical semantics for demos and tests without
any external API. Zero Gemini spend.
"""

from __future__ import annotations

import hashlib
import math

from .base import EmbeddingResult


class _DeterministicRandom:
    """Small deterministic PRNG (splitmix64) — stable across runs and platforms."""

    def __init__(self, seed: int):
        self._state = seed & 0xFFFFFFFFFFFFFFFF

    def next_double(self) -> float:
        self._state = (self._state + 0x9E3779B97F4A7C15) & 0xFFFFFFFFFFFFFFFF
        z = self._state
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
        z ^= z >> 31
        return (z / 0xFFFFFFFFFFFFFFFF) * 2.0 - 1.0


class MockEmbeddingProvider:
    provider_name = "mock"

    def __init__(self, *, dimension: int = 768, model: str = "mock-gemini-embedding-2"):
        if not isinstance(dimension, int) or dimension < 1:
            raise ValueError(f"Mock embedding dimension must be a positive integer, got {dimension!r}.")
        self._dimension = dimension
        self._model = model
        self._model_version = f"{model}@{dimension}d"

    @property
    def model(self) -> str:
        return self._model

    @property
    def model_version(self) -> str:
        return self._model_version

    @property
    def dimension(self) -> int:
        return self._dimension

    def embed_texts(self, texts: list[str]) -> EmbeddingResult:
        vectors = [self._embed_one(t) for t in texts]
        return EmbeddingResult(
            vectors=vectors,
            model=self._model,
            model_version=self._model_version,
            dimension=self._dimension,
            input_tokens=sum(len(t.split()) for t in texts),
            latency_ms=None,
        )

    def _embed_one(self, text: str) -> list[float]:
        # Character 4-gram overlap makes similar strings land near each other.
        # Each gram writes +1.0 into three hashed dimensions (multi-probe reduces
        # collisions); a small text-seeded noise floor keeps distinct texts distinct.
        grams = sorted({text[i : i + 4] for i in range(max(0, len(text) - 3))})
        vector = [0.0] * self._dimension
        for gram in grams:
            digest = hashlib.sha256(gram.encode("utf-8")).digest()
            for probe in range(3):
                seed = int.from_bytes(digest[probe * 8 : probe * 8 + 8], "big") ^ probe
                idx = seed % self._dimension
                vector[idx] += 1.0
        if not grams:
            # Empty text: pure seeded noise (still deterministic).
            digest = hashlib.sha256(text.encode("utf-8")).digest()
            rng = _DeterministicRandom(int.from_bytes(digest[:8], "big") ^ 0x5A17CE9B)
            vector = [rng.next_double() for _ in range(self._dimension)]
        norm = math.sqrt(sum(v * v for v in vector)) or 1.0
        return [v / norm for v in vector]
