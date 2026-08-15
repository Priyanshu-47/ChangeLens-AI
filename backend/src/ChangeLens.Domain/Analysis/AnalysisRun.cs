using ChangeLens.Domain.Common;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Domain.Analysis;

/// <summary>Canonical job states (docs/api-contract.md §5): Queued → Running → Succeeded | Failed.</summary>
public static class AnalysisStatus
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Queued, Running, Succeeded, Failed
    };

    public static bool IsValid(string status) => All.Contains(status);
}

/// <summary>Machine-readable failure codes persisted on failed runs (api-contract.md §5).</summary>
public static class AnalysisFailureCode
{
    public const string AiValidationFailed = "AI_VALIDATION_FAILED";
    public const string LlmRateLimited = "LLM_RATE_LIMITED";
    public const string AiTimeout = "AI_TIMEOUT";
    public const string AiUnavailable = "AI_UNAVAILABLE";
    public const string JobTimeout = "JOB_TIMEOUT";
    public const string QueueFull = "QUEUE_FULL";
    public const string WorkerInterrupted = "WORKER_INTERRUPTED";
    public const string ToolCallLimitExceeded = "TOOL_CALL_LIMIT_EXCEEDED";
    public const string Internal = "INTERNAL";
}

/// <summary>
/// AI observability record — one AI analysis run (domain-model.md, ADR-0009).
/// Stores enough to reproduce and audit an analysis (brief §9/§36): project, change
/// or incident identifier, model, prompt version, retrieval configuration, status,
/// timing, and the validated result. Raw prompts, API keys and secrets are never stored.
/// </summary>
public sealed class AnalysisRun : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    /// <summary>ChangeRisk or IncidentInvestigation (domain-model.md §23).</summary>
    public string Type { get; set; } = "ChangeRisk";

    /// <summary>One of <see cref="AnalysisStatus"/> (Queued / Running / Succeeded / Failed).</summary>
    public string Status { get; set; } = AnalysisStatus.Queued;

    /// <summary>Change identifier (e.g. "3df7783..working-tree" or a diff hash).</summary>
    public string? ChangeIdentifier { get; set; }

    /// <summary>The investigated incident, when Type == IncidentInvestigation.</summary>
    public Guid? IncidentId { get; set; }

    /// <summary>Client-generated idempotency key (api-contract.md §5.3); unique per project.</summary>
    public string? RequestId { get; set; }

    /// <summary>AI model that produced the result (from usage metadata).</summary>
    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    /// <summary>Retrieval configuration snapshot (top-k, RRF-k, budgets) as JSON.</summary>
    public string? RetrievalConfig { get; set; }

    /// <summary>Validated result (JSON) when Succeeded; never raw provider blobs or secrets.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Result schema version (e.g. "incident-v1") for reproducible evaluation.</summary>
    public string? ResultSchemaVersion { get; set; }

    /// <summary>Machine-readable failure code when Failed (see <see cref="AnalysisFailureCode"/>).</summary>
    public string? FailureCode { get; set; }

    /// <summary>Safe failure detail when Status == Failed (never raw stack traces or secrets).</summary>
    public string? Error { get; set; }

    /// <summary>Per-stage observability trace (JSON, Phase 7 — see docs/evaluation.md §5).
    /// Stages carry real wall-clock durations; retrieval items carry leg attribution.
    /// Raw prompts and secrets are never stored.</summary>
    public string? TraceJson { get; set; }

    /// <summary>Trace schema version (e.g. "trace-v1").</summary>
    public string? TraceSchemaVersion { get; set; }

    public DateTime? QueuedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public Project? Project { get; set; }

    /// <summary>
    /// Validates and applies a state transition (api-contract.md §5.2). Throws when the
    /// transition is invalid, so a stale or double-processed job can never move backwards
    /// (e.g. Succeeded → Running) or be re-completed.
    /// </summary>
    public void TransitionTo(string newStatus)
    {
        if (!AnalysisStatus.IsValid(newStatus))
        {
            throw new InvalidOperationException($"Unknown analysis status '{newStatus}'.");
        }

        var allowed = newStatus switch
        {
            AnalysisStatus.Running => Status is AnalysisStatus.Queued,
            AnalysisStatus.Succeeded => Status is AnalysisStatus.Running,
            AnalysisStatus.Failed => Status is AnalysisStatus.Queued or AnalysisStatus.Running,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Invalid analysis state transition '{Status}' -> '{newStatus}' (run {Id}).");
        }

        Status = newStatus;
    }
}
