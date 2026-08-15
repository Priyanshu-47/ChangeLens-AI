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

/// <summary>Internal AI-service request for incident investigation.</summary>
public sealed class IncidentAnalysisRequestDto
{
    public Guid AnalysisId { get; set; }

    public Guid ProjectId { get; set; }

    public IncidentContextDto Incident { get; set; } = new();

    public string? PromptVersion { get; set; }

    public int? MaxEvidenceChunks { get; set; }

    public int? MaxCharsPerChunk { get; set; }
}

/// <summary>Validated incident investigation result from the AI service.</summary>
public sealed class IncidentAnalysisResponseDto
{
    public string AnalysisType { get; set; } = "incident";

    public IncidentAnalysisResultDto Result { get; set; } = new();

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
