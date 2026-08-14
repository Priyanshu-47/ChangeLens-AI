"""Configuration: required secrets fail fast; defaults match Phase 0 decisions."""

import pytest
from pydantic import ValidationError

from app.config import Settings


def test_internal_api_key_is_required():
    with pytest.raises(ValidationError):
        Settings(internal_api_key="", ai_provider="mock")


def test_internal_api_key_min_length():
    with pytest.raises(ValidationError):
        Settings(internal_api_key="short", ai_provider="mock")


def test_gemini_provider_requires_api_key():
    with pytest.raises(ValueError, match="GEMINI_API_KEY"):
        Settings(internal_api_key="test-internal-key", ai_provider="gemini", gemini_api_key=None)


def test_mock_provider_needs_no_gemini_key():
    settings = Settings(internal_api_key="test-internal-key", ai_provider="mock")
    assert settings.ai_provider == "mock"


def test_default_model_is_phase0_decision():
    settings = Settings(internal_api_key="test-internal-key", ai_provider="mock")
    # Phase 0 (docs/llm-integration.md): current GA model, configurable via env.
    assert settings.gemini_text_model == "gemini-3.7-flash"


def test_model_is_configurable():
    settings = Settings(
        internal_api_key="test-internal-key",
        ai_provider="gemini",
        gemini_api_key="k",
        gemini_text_model="custom-model",
    )
    assert settings.gemini_text_model == "custom-model"


def test_invalid_provider_rejected():
    with pytest.raises(ValueError, match="AI_PROVIDER"):
        Settings(internal_api_key="test-internal-key", ai_provider="ollama")


def test_retry_and_repair_bounds():
    with pytest.raises(ValueError, match="GEMINI_MAX_RETRIES"):
        Settings(internal_api_key="test-internal-key", ai_provider="mock", gemini_max_retries=99)
    with pytest.raises(ValueError, match="AI_MAX_REPAIR_ATTEMPTS"):
        Settings(internal_api_key="test-internal-key", ai_provider="mock", ai_max_repair_attempts=0)


def test_readiness_probe_defaults_off():
    settings = Settings(internal_api_key="test-internal-key", ai_provider="mock")
    assert settings.ai_readiness_probe is False
