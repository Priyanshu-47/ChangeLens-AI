namespace ChangeLens.Application.Configuration;

/// <summary>
/// In-memory rate limiting for analysis submission endpoints (Phase 9 hardening).
/// Single-instance by design (the MVP job queue is in-process); a multi-instance
/// deployment must move this to a shared store (documented in docs/security-model.md).
/// Health and read endpoints are deliberately NOT rate-limited.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Maximum analysis submissions per user/IP within the window.</summary>
    public int AnalysisPermitLimit { get; set; } = 10;

    /// <summary>Window length in seconds.</summary>
    public int AnalysisWindowSeconds { get; set; } = 60;

    /// <summary>Number of queued requests allowed after the limit is hit (burst absorption).</summary>
    public int AnalysisQueueLimit { get; set; } = 4;
}
