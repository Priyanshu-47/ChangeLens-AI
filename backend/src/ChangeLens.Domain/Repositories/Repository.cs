using ChangeLens.Domain.Common;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Domain.Repositories;

/// <summary>
/// A registered source repository (metadata only in Phase 1; git URL or local path).
/// Soft-deleted when removed so analysis history stays coherent.
/// </summary>
public sealed class Repository : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? DefaultBranch { get; set; }

    public string Language { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public Project? Project { get; set; }
}
