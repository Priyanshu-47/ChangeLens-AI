"""Retry semantics: transient only, bounded, exponential backoff with jitter."""

import asyncio

import pytest

from app.providers.base import ProviderAuthError, ProviderRateLimited, ProviderUnavailable
from app.providers.retry import backoff_delay, with_retry


@pytest.mark.asyncio
async def test_retries_transient_then_succeeds():
    calls = 0

    async def flaky():
        nonlocal calls
        calls += 1
        if calls < 3:
            raise ProviderUnavailable("boom")
        return "ok"

    result = await with_retry(flaky, max_retries=3)
    assert result == "ok"
    assert calls == 3


@pytest.mark.asyncio
async def test_rate_limited_uses_provided_retry_after():
    calls = 0

    async def flaky():
        nonlocal calls
        calls += 1
        raise ProviderRateLimited("limit", retry_after_seconds=0.01)

    with pytest.raises(ProviderRateLimited):
        await with_retry(flaky, max_retries=2, retry_after=lambda e: e.retry_after_seconds)
    assert calls == 3  # initial + 2 retries


@pytest.mark.asyncio
async def test_exhausts_retries_and_propagates_last_error():
    async def always_fails():
        raise ProviderUnavailable("still down")

    with pytest.raises(ProviderUnavailable):
        await with_retry(always_fails, max_retries=2)


@pytest.mark.asyncio
async def test_does_not_retry_non_transient_errors():
    calls = 0

    async def auth_fail():
        nonlocal calls
        calls += 1
        raise ProviderAuthError("bad key")

    with pytest.raises(ProviderAuthError):
        await with_retry(auth_fail, max_retries=3)
    assert calls == 1


@pytest.mark.asyncio
async def test_no_retries_when_max_is_zero():
    calls = 0

    async def flaky():
        nonlocal calls
        calls += 1
        raise ProviderUnavailable("boom")

    with pytest.raises(ProviderUnavailable):
        await with_retry(flaky, max_retries=0)
    assert calls == 1


def test_backoff_delay_grows_exponentially_within_cap():
    d1 = backoff_delay(0, base=1.0)
    d2 = backoff_delay(1, base=1.0)
    d3 = backoff_delay(2, base=1.0)
    assert d2 > d1
    assert d3 > d2
    # cap applies at high attempts
    d10 = backoff_delay(10, base=1.0, cap=8.0, jitter=0.0)
    assert d10 == 8.0
