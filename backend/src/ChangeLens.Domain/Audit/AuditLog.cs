namespace ChangeLens.Domain.Audit;

/// <summary>
/// Append-only audit trail. Written for auth events, mutations, and (later) AI tool calls.
/// No API path allows editing or deleting these rows.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime OccurredAtUtc { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>Project the event belongs to (null for non-project events such as login).</summary>
    public Guid? ProjectId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public string? ResourceId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Arbitrary structured detail stored as a jsonb column.</summary>
    public string? DetailsJson { get; set; }
}
