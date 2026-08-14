using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ChangeLens.Domain.Incidents;

namespace ChangeLens.Application.Dtos;

public sealed class CreateIncidentRequest
{
    [Required]
    public Guid ProjectId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Sev3;

    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    [MaxLength(200)]
    public string? Classification { get; set; }

    public Guid? AffectedServiceId { get; set; }

    [MaxLength(100)]
    public string? Environment { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? DetectedAtUtc { get; set; }

    [MaxLength(4000)]
    public string? Summary { get; set; }

    public List<CreateIncidentEventRequest> Events { get; set; } = [];
}

public sealed class CreateIncidentEventRequest
{
    public DateTime? OccurredAtUtc { get; set; }

    [Required, EnumDataType(typeof(IncidentEventType))]
    public IncidentEventType Type { get; set; }

    [MaxLength(200)]
    public string? Source { get; set; }

    [MaxLength(4000)]
    public string? Message { get; set; }

    public JsonElement? RawData { get; set; }
}

public sealed class UpdateIncidentRequest
{
    [MaxLength(200)]
    public string? Title { get; set; }

    public IncidentSeverity? Severity { get; set; }

    public IncidentStatus? Status { get; set; }

    [MaxLength(200)]
    public string? Classification { get; set; }

    [MaxLength(100)]
    public string? Environment { get; set; }

    [MaxLength(4000)]
    public string? Summary { get; set; }
}

public class IncidentListItemResponse
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public string Title { get; init; } = string.Empty;

    public IncidentSeverity Severity { get; init; }

    public IncidentStatus Status { get; init; }

    public string? Classification { get; init; }

    public Guid? AffectedServiceId { get; init; }

    public string? Environment { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? DetectedAtUtc { get; init; }

    public string? Summary { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class IncidentEventResponse
{
    public Guid Id { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public IncidentEventType Type { get; init; }

    public string? Source { get; init; }

    public string? Message { get; init; }

    public JsonElement? RawData { get; init; }
}

public sealed class IncidentResponse : IncidentListItemResponse
{
    public IReadOnlyList<IncidentEventResponse> Events { get; init; } = [];
}
