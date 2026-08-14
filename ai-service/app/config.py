"""Application configuration.

Everything is environment-driven (pydantic-settings). Required secrets fail fast with a
clear message at startup; unknown values are ignored. See .env.example for the full list.
"""

from functools import lru_cache

from pydantic import Field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Validated configuration. Constructing a Settings instance IS the startup check."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        case_sensitive=False,
    )

    # --- Service ---
    app_name: str = "changelens-ai"
    log_level: str = "INFO"

    # --- Internal API auth (shared secret with the ASP.NET Core backend) ---
    internal_api_key: str = Field(..., min_length=8)

    # --- Provider selection ---
    # "gemini" (default, real LLM) or "mock" (deterministic local stand-in for dev/tests).
    ai_provider: str = "gemini"

    # --- Gemini (required only when ai_provider == "gemini") ---
    gemini_api_key: str | None = None
    # Default model is a Phase 0 decision (docs/llm-integration.md): a current GA model,
    # configurable via env. Never hardcode a model inside business logic.
    gemini_text_model: str = "gemini-3.7-flash"
    gemini_timeout_seconds: float = 60.0
    gemini_max_retries: int = 3
    gemini_max_output_tokens: int = 8192
    # Optional per-model pricing (USD per 1M tokens). When unset, no cost estimate is
    # produced — we never fabricate pricing.
    gemini_input_price_per_1m_usd: float | None = None
    gemini_output_price_per_1m_usd: float | None = None

    # --- Analysis behaviour ---
    ai_max_repair_attempts: int = 2
    # Live model-resolution probe on /ready. Default off: health must never consume
    # Gemini calls or tokens. When on, /ready resolves the configured model name.
    ai_readiness_probe: bool = False
    # Hard cap on evidence content rendered into a prompt (token-budget guard).
    ai_max_evidence_chars: int = 120_000

    @model_validator(mode="after")
    def _validate(self) -> "Settings":
        if self.ai_provider not in ("gemini", "mock"):
            raise ValueError(
                f"AI_PROVIDER must be 'gemini' or 'mock', got {self.ai_provider!r}."
            )
        if self.ai_provider == "gemini" and not self.gemini_api_key:
            raise ValueError(
                "GEMINI_API_KEY is required when AI_PROVIDER=gemini. Set GEMINI_API_KEY, "
                "or use AI_PROVIDER=mock for local development without a key."
            )
        if not 1 <= self.gemini_max_retries <= 5:
            raise ValueError("GEMINI_MAX_RETRIES must be between 1 and 5.")
        if not 1 <= self.ai_max_repair_attempts <= 5:
            raise ValueError("AI_MAX_REPAIR_ATTEMPTS must be between 1 and 5.")
        if not 0 < self.gemini_timeout_seconds <= 300:
            raise ValueError("GEMINI_TIMEOUT_SECONDS must be in (0, 300].")
        return self


@lru_cache
def get_settings() -> Settings:
    """Cached settings for the ASGI entrypoint. Tests build Settings directly."""
    return Settings()
