using ChangeLens.Domain.Common;

namespace ChangeLens.Domain.Projects;

/// <summary>
/// A workspace that isolates all data (incidents, repositories, services, analyses).
/// Soft-deleted, never removed from the database while history matters.
/// </summary>
public sealed class Project : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>URL-safe unique identifier derived from the name.</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsDeleted { get; set; }
}
