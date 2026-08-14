using ChangeLens.Domain.Common;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Domain.Services;

/// <summary>
/// A deployable unit within a project (e.g. "auth-api"). Incidents reference a
/// service; later phases attach components and deployments to it.
/// </summary>
public sealed class Service : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Language { get; set; }

    public string? RootPath { get; set; }

    public Project? Project { get; set; }
}
