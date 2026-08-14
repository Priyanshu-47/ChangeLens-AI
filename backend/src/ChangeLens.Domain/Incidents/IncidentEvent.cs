namespace ChangeLens.Domain.Incidents;

/// <summary>
/// One entry on an incident's timeline: an error, log excerpt, deployment, or metric.
/// <see cref="RawDataJson"/> carries arbitrary structured payload (stack traces, JSON logs)
/// and is stored as a jsonb column.
/// </summary>
public sealed class IncidentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public IncidentEventType Type { get; set; }

    public string? Source { get; set; }

    public string? Message { get; set; }

    public string? RawDataJson { get; set; }

    public Incident? Incident { get; set; }
}
