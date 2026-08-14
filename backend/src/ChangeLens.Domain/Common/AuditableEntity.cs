namespace ChangeLens.Domain.Common;

/// <summary>
/// Base for entities that carry creation/update timestamps (UTC).
/// Timestamps are set centrally by the persistence layer on save.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}
