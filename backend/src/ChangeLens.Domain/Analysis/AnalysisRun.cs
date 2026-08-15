using ChangeLens.Domain.Common;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Domain.Analysis;

/// <summary>
/// AI observability record — one AI analysis run (domain-model.md, ADR-0009).
/// Stores enough to reproduce and audit an analysis (brief §36): project, change
/// identifier, model, prompt version, retrieval configuration, status, and timing.
/// Raw prompts and secrets are never stored.
/// </summary>
public sealed class AnalysisRun : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    /// <summary>ChangeRisk or IncidentInvestigation (domain-model.md §23).</summary>
    public string Type { get; set; } = "ChangeRisk";

    /// <summary>Queued / Running / Succeeded / Failed.</summary>
    public string Status { get; set; } = "Running";

    /// <summary>Change identifier (e.g. "3df7783..working-tree" or a diff hash).</summary>
    public string? ChangeIdentifier { get; set; }

    /// <summary>AI model that produced the result (from usage metadata).</summary>
    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    /// <summary>Retrieval configuration snapshot (top-k, RRF-k, budgets) as JSON.</summary>
    public string? RetrievalConfig { get; set; }

    /// <summary>Failure detail when Status == Failed (never raw secrets or full source).</summary>
    public string? Error { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public Project? Project { get; set; }
}
