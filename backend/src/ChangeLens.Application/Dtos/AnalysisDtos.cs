using ChangeLens.Domain.Analysis;

namespace ChangeLens.Application.Dtos;

/// <summary>One unit of async work (docs/rag-architecture.md §6, ADR-0009).</summary>
public sealed record AnalysisJob(Guid AnalysisRunId, Guid ProjectId, Guid IncidentId, string? RequestId);

/// <summary>POST /api/v1/incidents/{incidentId}/investigate body — optional idempotency key.</summary>
public sealed class InvestigateIncidentRequest
{
    /// <summary>Client-generated idempotency key (api-contract.md §5.3). Optional.</summary>
    public string? RequestId { get; set; }
}

/// <summary>202 Accepted body for a submitted investigation.</summary>
public sealed class InvestigationAcceptedResponse
{
    public Guid AnalysisId { get; set; }

    public string Status { get; set; } = "Queued";

    public string StatusUrl { get; set; } = string.Empty;
}

/// <summary>GET /api/v1/analyses/{analysisId} response (job resource, api-contract.md §5).</summary>
public sealed class AnalysisStatusResponse
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public Guid? IncidentId { get; set; }

    /// <summary>Validated result when Succeeded (parsed from the persisted JSON); null otherwise.</summary>
    public object? Result { get; set; }

    public string? ResultSchemaVersion { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public DateTime? QueuedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public AnalysisErrorDto? Error { get; set; }
}

/// <summary>Safe failure representation (never raw stack traces or secrets).</summary>
public sealed class AnalysisErrorDto
{
    public string? Code { get; set; }

    public string? Message { get; set; }
}

// ── Internal incident investigation contract (ai-service-boundary.md §"POST /internal/v1/analysis/incident") ──

/// <summary>Normalized investigation context assembled by the backend (brief §12).</summary>
public sealed class IncidentContextDto
{
    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string Severity { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Environment { get; set; }

    public string? Service { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? DetectedAtUtc { get; set; }

    /// <summary>Chronologically ordered timeline (brief §11).</summary>
    public List<TimelineEventDto> Timeline { get; set; } = [];

    public List<string> Symptoms { get; set; } = [];

    public List<string> KnownFacts { get; set; } = [];

    public List<string> Unknowns { get; set; } = [];
}

/// <summary>One normalized timeline event (existing IncidentEvent conventions).</summary>
public sealed class TimelineEventDto
{
    public DateTime? OccurredAtUtc { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Source { get; set; }

    public string? Message { get; set; }

    public string? RawData { get; set; }
}

/// <summary>One allowlisted tool the AI may propose (Phase 8, docs/agent-tools.md).</summary>
public sealed class ToolDefinitionDto
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>JSON Schema for the tool's arguments (sent to the AI service for prompting).</summary>
    public Dictionary<string, object?> InputSchema { get; set; } = new();
}

/// <summary>A tool proposal the AI made this turn.</summary>
public sealed class ToolCallDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Dictionary<string, object?> Arguments { get; set; } = new();
}

/// <summary>Result of one executed/rejected tool call, fed back to the AI (untrusted).</summary>
public sealed class ToolResultItemDto
{
    public string ToolCallId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    /// <summary>executed | rejected | failed | not_allowed | timeout</summary>
    public string Status { get; set; } = "executed";

    /// <summary>Sanitized JSON output (never raw exceptions or secrets).</summary>
    public string? Output { get; set; }

    public string? ErrorCode { get; set; }
}

/// <summary>Internal AI-service request for incident investigation.</summary>
public sealed class IncidentAnalysisRequestDto
{
    public Guid AnalysisId { get; set; }

    public Guid ProjectId { get; set; }

    public IncidentContextDto Incident { get; set; } = new();

    public string? PromptVersion { get; set; }

    public int? MaxEvidenceChunks { get; set; }

    public int? MaxCharsPerChunk { get; set; }

    /// <summary>Phase 8 tool loop: the allowlist sent to the AI service (proposals only).</summary>
    public List<ToolDefinitionDto> ToolCatalog { get; set; } = [];

    /// <summary>Accumulated tool results fed back into the next turn.</summary>
    public List<ToolResultItemDto> ToolResults { get; set; } = [];
}

/// <summary>One turn of the tool loop returned by the AI service.</summary>
public sealed class IncidentAnalysisResponseDto
{
    public string AnalysisType { get; set; } = "incident";

    /// <summary>final | tool_call (Phase 8). Defaults to final for pre-tool contracts.</summary>
    public string Kind { get; set; } = "final";

    public ToolCallDto? ToolCall { get; set; }

    public IncidentAnalysisResultDto? Result { get; set; }

    public AnalysisUsageDto Usage { get; set; } = new();

    /// <summary>Retrieval trace from the AI service (Phase 7 observability).</summary>
    public RetrievalTraceDto? Trace { get; set; }
}

// ── Analysis trace API (Phase 7, docs/evaluation.md §5) ────────────────────

/// <summary>Normalized failure categories (docs/evaluation.md §6).</summary>
public static class AnalysisFailureCategory
{
    public const string Validation = "VALIDATION";
    public const string Authorization = "AUTHORIZATION";
    public const string Retrieval = "RETRIEVAL";
    public const string AiProvider = "AI_PROVIDER";
    public const string RateLimit = "RATE_LIMIT";
    public const string Timeout = "TIMEOUT";
    public const string Persistence = "PERSISTENCE";
    public const string Internal = "INTERNAL";

    /// <summary>Maps a persisted failure code to its normalized category.</summary>
    public static string For(string? failureCode) => failureCode switch
    {
        AnalysisFailureCode.AiValidationFailed => Validation,
        AnalysisFailureCode.LlmRateLimited => RateLimit,
        AnalysisFailureCode.AiTimeout => Timeout,
        AnalysisFailureCode.AiUnavailable => AiProvider,
        AnalysisFailureCode.JobTimeout => Timeout,
        AnalysisFailureCode.ToolCallLimitExceeded => Validation,
        _ => Internal
    };
}

/// <summary>One timed stage in the analysis trace.</summary>
public sealed class AnalysisStageDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Completed | Failed.</summary>
    public string Status { get; set; } = "Completed";

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Real wall-clock duration; never estimated.</summary>
    public long? DurationMs { get; set; }

    /// <summary>Stage-specific metadata (e.g. { failureCode, failureCategory }).</summary>
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>One tool call recorded in the analysis trace (Phase 8, docs/agent-tools.md).</summary>
public sealed class ToolCallTraceDto
{
    public string ToolCallId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    /// <summary>Proposed | Validated | Executed | Rejected | Failed.</summary>
    public string Status { get; set; } = "Proposed";

    /// <summary>Real wall-clock execution duration (never estimated).</summary>
    public long? DurationMs { get; set; }

    /// <summary>Truncated argument summary (identifiers only, never secrets).</summary>
    public string? Arguments { get; set; }

    public string? ErrorCode { get; set; }

    /// <summary>Count of evidence ids the tool attached to its output.</summary>
    public int? EvidenceIdCount { get; set; }
}

/// <summary>GET /api/v1/analyses/{analysisId}/trace response.</summary>
public sealed class AnalysisTraceResponse
{
    public Guid AnalysisId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public string? ResultSchemaVersion { get; set; }

    public string? TraceSchemaVersion { get; set; }

    public List<AnalysisStageDto> Stages { get; set; } = [];

    public RetrievalTraceDto? Retrieval { get; set; }

    public List<ToolCallTraceDto> ToolCalls { get; set; } = [];

    public string? FailureCode { get; set; }

    public string? FailureCategory { get; set; }
}

public sealed class IncidentAnalysisResultDto
{
    public List<RootCauseCandidateDto> RootCauseCandidates { get; set; } = [];

    public RemediationDto Remediation { get; set; } = new();

    public List<string> Unknowns { get; set; } = [];

    public List<EvidenceItemDto> Evidence { get; set; } = [];
}

public sealed class RootCauseCandidateDto
{
    public string? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public double Confidence { get; set; }

    /// <summary>Candidate | Confirmed | Dismissed.</summary>
    public string Status { get; set; } = "Candidate";

    /// <summary>Grounded: every candidate must reference >=1 evidence index id.</summary>
    public List<string> EvidenceIds { get; set; } = [];

    public string? Reasoning { get; set; }

    public List<string> Unknowns { get; set; } = [];
}

public sealed class RemediationDto
{
    public string? ImmediateMitigation { get; set; }

    public List<string> InvestigationSteps { get; set; } = [];

    public string? RecommendedRemediation { get; set; }

    public List<string> ValidationSteps { get; set; } = [];

    public string? RollbackConsideration { get; set; }

    public bool InsufficientEvidence { get; set; }
}
