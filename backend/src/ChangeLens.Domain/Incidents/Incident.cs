using ChangeLens.Domain.Common;
using ChangeLens.Domain.Projects;
using ChangeLens.Domain.Services;

namespace ChangeLens.Domain.Incidents;

/// <summary>
/// The incident record submitted for investigation. Timeline entries live in
/// <see cref="IncidentEvent"/>. Phase 4 adds the AI investigation on top of this.
/// </summary>
public sealed class Incident : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Sev3;

    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    public string? Classification { get; set; }

    public Guid? AffectedServiceId { get; set; }

    public string? Environment { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? DetectedAtUtc { get; set; }

    public string? Summary { get; set; }

    public Project? Project { get; set; }

    public Service? AffectedService { get; set; }

    public List<IncidentEvent> Events { get; set; } = [];
}
