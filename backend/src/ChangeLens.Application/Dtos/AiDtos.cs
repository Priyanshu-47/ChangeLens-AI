using System.ComponentModel.DataAnnotations;

namespace ChangeLens.Application.Dtos;

/// <summary>
/// Request for a change-risk analysis. Mirrors the AI service evidence-package
/// contract (docs/ai-service-boundary.md §3); the AI service fills defaults for the
/// retrieval-backed sections (impacted components, retrieved documents, …) which the
/// backend assembles in Phase 4.
/// </summary>
public sealed class AnalyzeChangeRiskRequest
{
    [Required]
    public Guid ProjectId { get; set; }

    [Required, MaxLength(5000)]
    public string ChangeSummary { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(200)]
    public List<ChangedFileRequest> ChangedFiles { get; set; } = [];

    public string SchemaVersion { get; set; } = "1";

    [MaxLength(100)]
    public string? PromptVersion { get; set; }
}

public sealed class ChangedFileRequest
{
    [Required, MaxLength(1000)]
    public string Path { get; set; } = string.Empty;

    [MaxLength(20)]
    public string ChangeType { get; set; } = "modified";

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(200)]
    public List<string> SymbolsChanged { get; set; } = [];

    [MaxLength(20000)]
    public string? DiffPreview { get; set; }
}

/// <summary>Validated risk report + usage metadata returned by the AI service.</summary>
public sealed class ChangeRiskAnalysisResponse
{
    public string AnalysisType { get; init; } = "change-risk";

    public ChangeRiskResultDto Result { get; init; } = new();

    public AnalysisUsageDto Usage { get; init; } = new();
}

public sealed class ChangeRiskResultDto
{
    public string RiskLevel { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public List<ImpactedComponentDto> ImpactedComponents { get; init; } = [];

    public List<RiskFactorDto> RiskFactors { get; init; } = [];

    public List<HistoricalIncidentDto> HistoricalIncidents { get; init; } = [];

    public List<RecommendedTestDto> RecommendedTests { get; init; } = [];

    public List<string> Unknowns { get; init; } = [];

    public List<EvidenceItemDto> Evidence { get; init; } = [];
}

public sealed class ImpactedComponentDto
{
    public string? ComponentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Service { get; init; }
    public string? FilePath { get; init; }
    public string Impact { get; init; } = "MODIFIED";
}

public sealed class EvidenceReferenceDto
{
    public string Type { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

public sealed class RiskFactorDto
{
    public string? Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public List<EvidenceReferenceDto> Evidence { get; init; } = [];
    public List<string> Unknowns { get; init; } = [];
}

public sealed class HistoricalIncidentDto
{
    public string? IncidentId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public double? Similarity { get; init; }
    public string? Summary { get; init; }
    public string? Evidence { get; init; }
}

public sealed class RecommendedTestDto
{
    public string Category { get; init; } = string.Empty;
    public string? TargetComponent { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed class EvidenceItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? AiDocumentId { get; init; }
}

public sealed class AnalysisUsageDto
{
    public string? Model { get; init; }
    public string? PromptVersion { get; init; }
    public long? LatencyMs { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public double? EstimatedCostUsd { get; init; }
    public string ValidationStatus { get; init; } = "valid";
    public int RepairAttempts { get; init; }
    public bool EvidenceTruncated { get; init; }
}
