"""Bounded retry with exponential backoff + jitter for transient provider failures.

Rules (docs/llm-integration.md §2, ai-service-boundary.md §4):
- retry ONLY on transient conditions: 429, 5xx, transport/connection errors
- NEVER retry auth failures, malformed requests, or validation failures
- bounded by an explicit attempt count; no infinite loops
- exponential backoff with random jitter (and the provider's retry-after when given)
"""

from __future__ import annotations

import asyncio
import random
from collections.abc import Awaitable, Callable, Coroutine
from typing import Any, TypeVar

from .base import ProviderError, ProviderRateLimited, ProviderUnavailable

T = TypeVar("T")

_RETRYABLE: tuple[type[ProviderError], ...] = (ProviderRateLimited, ProviderUnavailable)


def backoff_delay(attempt: int, *, base: float = 1.0, cap: float = 8.0, jitter: float = 0.25) -> float:
    """Exponential backoff with jitter: base * 2^attempt, capped, plus jitter."""
    exponential = min(base * (2**attempt), cap)
    return exponential + random.uniform(0, jitter * exponential)


async def with_retry(
    fn: Callable[[], Awaitable[T]],
    *,
    max_retries: int,
    is_retryable: Callable[[Exception], bool] | None = None,
    retry_after: Callable[[Exception], float | None] | None = None,
) -> T:
    """Run `fn`, retrying transient failures up to `max_retries` times with backoff.

    Non-retryable exceptions propagate immediately. After exhausting retries the last
    exception propagates to the caller (which maps it to the HTTP error contract).
    """
    attempt = 0
    while True:
        try:
            return await fn()
        except Exception as exc:  # noqa: BLE001 - classification decides what to do
            if not _is_retryable(exc, is_retryable):
                raise
            if attempt >= max_retries:
                raise
            delay = backoff_delay(attempt)
            if retry_after is not None:
                provided = retry_after(exc)
                if provided is not None:
                    delay = min(max(provided, 0.1), 10.0)
            await asyncio.sleep(delay)
            attempt += 1


def _is_retryable(exc: Exception, custom: Callable[[Exception], bool] | None) -> bool:
    if custom is not None:
        return custom(exc)
    return isinstance(exc, _RETRYABLE)
