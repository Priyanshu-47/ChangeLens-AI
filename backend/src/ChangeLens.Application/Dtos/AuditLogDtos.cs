using System.Text.Json;

namespace ChangeLens.Application.Dtos;

public sealed class AuditLogResponse
{
    public Guid Id { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public Guid? UserId { get; init; }

    public Guid? ProjectId { get; init; }

    public string Action { get; init; } = string.Empty;

    public string ResourceType { get; init; } = string.Empty;

    public string? ResourceId { get; init; }

    public string? IpAddress { get; init; }

    public JsonElement? Details { get; init; }
}
