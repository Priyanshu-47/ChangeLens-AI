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
    # Automatically run hybrid retrieval to fill the evidence package when the request
    # does not already contain retrieved documents (Phase 3 wiring of RAG into analysis).
    ai_auto_retrieve: bool = True

    # --- Persistence (ai schema only — the app schema is owned by .NET, ADR-0003) ---
    database_url: str = "postgresql+psycopg://changelens@127.0.0.1:5433/changelens"
    database_echo: bool = False

    # --- Embeddings ---
    # "gemini" (real embedding API) or "mock" (deterministic vectors, $0 dev/tests).
    embedding_provider: str = "gemini"
    gemini_embedding_model: str = "text-embedding-004"
    # Dimension comes from the configured embedding model (text-embedding-004 -> 768).
    # It is config, not code; a model change ⇒ re-index (docs/llm-integration.md §2).
    embedding_dimension: int = 768
    embedding_batch_size: int = 32
    embedding_batch_max_retries: int = 3
    # Optional per-model embedding pricing (USD per 1M tokens) for cost estimation.
    gemini_embedding_price_per_1m_usd: float | None = None

    # --- Retrieval ---
    retrieval_top_k: int = 10
    retrieval_candidate_k: int = 50
    rrf_k: int = 60

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
        if self.embedding_provider not in ("gemini", "mock"):
            raise ValueError(
                f"EMBEDDING_PROVIDER must be 'gemini' or 'mock', got {self.embedding_provider!r}."
            )
        if self.embedding_provider == "gemini" and not self.gemini_api_key:
            raise ValueError(
                "GEMINI_API_KEY is required when EMBEDDING_PROVIDER=gemini. Set GEMINI_API_KEY, "
                "or use EMBEDDING_PROVIDER=mock for local development without a key."
            )
        if not 1 <= self.embedding_dimension <= 2000:
            raise ValueError("EMBEDDING_DIMENSION must be in (0, 2000] (HNSW upper bound).")
        if not 1 <= self.gemini_max_retries <= 5:
            raise ValueError("GEMINI_MAX_RETRIES must be between 1 and 5.")
        if not 1 <= self.ai_max_repair_attempts <= 5:
            raise ValueError("AI_MAX_REPAIR_ATTEMPTS must be between 1 and 5.")
        if not 0 < self.gemini_timeout_seconds <= 300:
            raise ValueError("GEMINI_TIMEOUT_SECONDS must be in (0, 300].")
        if not 1 <= self.retrieval_top_k <= 100:
            raise ValueError("RETRIEVAL_TOP_K must be in [1, 100].")
        return self


@lru_cache
def get_settings() -> Settings:
    """Cached settings for the ASGI entrypoint. Tests build Settings directly."""
    return Settings()
