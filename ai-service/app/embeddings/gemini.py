"""Gemini embedding provider via the google-genai SDK.

Batches texts, retries only transient failures (429/5xx) with exponential backoff,
and validates the returned dimension. Token counts come from SDK usage metadata
when present; null means "not exposed" — never guessed.
"""

from __future__ import annotations

import logging
import time

from google import genai
from google.genai import errors as genai_errors
from google.genai import types

from .base import (
    EmbeddingAuthError,
    EmbeddingDimensionError,
    EmbeddingRateLimited,
    EmbeddingResult,
    EmbeddingUnavailable,
)

logger = logging.getLogger(__name__)


class GeminiEmbeddingProvider:
    def __init__(
        self,
        *,
        api_key: str,
        model: str,
        dimension: int,
        batch_size: int = 32,
        timeout_seconds: float = 60.0,
        max_retries: int = 3,
    ):
        if not api_key:
            raise ValueError("GeminiEmbeddingProvider requires an API key.")
        self._model = model
        self._dimension = dimension
        self._batch_size = batch_size
        self._max_retries = max_retries
        # model_version must change when the embedding configuration changes, so
        # stale embeddings are detectable and re-indexable.
        self._model_version = f"{model}@{dimension}d"
        self._client = genai.Client(
            api_key=api_key,
            http_options=types.HttpOptions(timeout=int(timeout_seconds * 1000)),
        )

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
        started = time.perf_counter()
        vectors: list[list[float]] = []
        input_tokens: int | None = None

        for batch in _chunks(texts, self._batch_size):
            response = self._call_with_retry(batch)
            for item in response.embeddings or []:
                vector = list(item.values or [])
                if len(vector) != self._dimension:
                    raise EmbeddingDimensionError(
                        f"Provider returned {len(vector)}-dim vector, expected {self._dimension}."
                    )
                vectors.append(vector)
            usage = getattr(response, "usage_metadata", None)
            if usage is not None and usage.total_token_count is not None:
                input_tokens = (input_tokens or 0) + usage.total_token_count

        return EmbeddingResult(
            vectors=vectors,
            model=self._model,
            model_version=self._model_version,
            dimension=self._dimension,
            input_tokens=input_tokens,
            latency_ms=int((time.perf_counter() - started) * 1000),
        )

    def _call_with_retry(self, texts: list[str]):
        attempt = 0
        while True:
            try:
                # Pass one explicit Content per text: the SDK collapses a plain list of
                # strings into a SINGLE content (one embedding for N texts), which would
                # silently shrink every batch. Verified against gemini-embedding-2.
                contents = [
                    types.Content(parts=[types.Part(text=text)]) for text in texts
                ]
                return self._client.models.embed_content(
                    model=self._model,
                    contents=contents,
                    config=types.EmbedContentConfig(
                        output_dimensionality=self._dimension,
                    ),
                )
            except genai_errors.ClientError as exc:
                code = getattr(exc, "code", None)
                if code == 429:
                    if attempt >= self._max_retries:
                        raise EmbeddingRateLimited("Gemini embedding rate limit exceeded.") from exc
                    delay = _backoff(attempt)
                    time.sleep(delay)
                elif code in (400, 401, 403):
                    raise EmbeddingAuthError(f"Gemini rejected embedding request (HTTP {code}).") from exc
                elif code is not None and 500 <= code < 600:
                    if attempt >= self._max_retries:
                        raise EmbeddingUnavailable(f"Gemini embedding HTTP {code}.") from exc
                    time.sleep(_backoff(attempt))
                else:
                    raise EmbeddingUnavailable(
                        f"Gemini embedding error (HTTP {code or 'unknown'})."
                    ) from exc
            except Exception as exc:
                # Transport-level errors are treated as transient and retried.
                if attempt >= self._max_retries:
                    raise EmbeddingUnavailable(
                        f"Gemini embedding transport error: {type(exc).__name__}."
                    ) from exc
                time.sleep(_backoff(attempt))
            attempt += 1


def _chunks(items: list[str], size: int):
    for i in range(0, len(items), size):
        yield items[i : i + size]


def _backoff(attempt: int, *, base: float = 1.0, cap: float = 8.0) -> float:
    import random

    exponential = min(base * (2**attempt), cap)
    return exponential + random.uniform(0, 0.25 * exponential)
