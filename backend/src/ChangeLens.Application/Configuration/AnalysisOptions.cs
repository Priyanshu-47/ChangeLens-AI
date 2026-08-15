namespace ChangeLens.Application.Configuration;

/// <summary>
/// Async analysis configuration (ADR-0009). The MVP queue is in-process and bounded;
/// concurrency is capped to respect free-tier AI limits (brief §25). Retries apply only
/// to transient AI failures (brief §26).
/// </summary>
public sealed class AnalysisOptions
{
    public const string SectionName = "Analysis";

    /// <summary>Bounded job queue capacity (brief §24).</summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>Maximum concurrent AI analyses (brief §25; ANALYSIS_MAX_CONCURRENCY).</summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>Hard per-job timeout; a job cannot stay Running forever (brief §27).</summary>
    public int JobTimeoutSeconds { get; set; } = 600;

    /// <summary>Transient-failure retries (bounded; 400/401/403/validation never retried).</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base backoff between transient retries (exponential, capped at 30s).</summary>
    public int RetryBackoffSeconds { get; set; } = 5;

    /// <summary>
    /// On startup, mark interrupted Running runs as Failed(WORKER_INTERRUPTED) and
    /// re-enqueue Queued runs that were persisted but never processed.
    /// </summary>
    public bool RecoverOnStartup { get; set; } = true;
}
