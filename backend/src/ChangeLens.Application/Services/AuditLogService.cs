using System.Text.Json;
using ChangeLens.Application.Common;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Audit;
using Microsoft.Extensions.Logging;

namespace ChangeLens.Application.Services;

/// <summary>
/// Writes append-only audit entries and queries them per project.
/// A failed audit write must never break the business operation, so writes are
/// best-effort (logged, swallowed) — reads are exact.
/// </summary>
public sealed class AuditLogService(IAppDbContext db, ILogger<AuditLogService> logger)
{
    public async Task WriteAsync(
        string action,
        string resourceType,
        Guid? userId,
        Guid? projectId,
        Guid? resourceId = null,
        string? ipAddress = null,
        object? details = null,
        CancellationToken ct = default)
    {
        var entry = new AuditLog
        {
            OccurredAtUtc = DateTime.UtcNow,
            Action = action,
            ResourceType = resourceType,
            UserId = userId,
            ProjectId = projectId,
            ResourceId = resourceId?.ToString(),
            IpAddress = ipAddress,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details)
        };

        db.Set<AuditLog>().Add(entry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audit write failed and was swallowed for action {Action}", action);
        }
    }

    public async Task<PagedResult<AuditLogResponse>> QueryAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = db.Set<AuditLog>()
            .Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.OccurredAtUtc);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogResponse
            {
                Id = a.Id,
                OccurredAtUtc = a.OccurredAtUtc,
                UserId = a.UserId,
                ProjectId = a.ProjectId,
                Action = a.Action,
                ResourceType = a.ResourceType,
                ResourceId = a.ResourceId,
                IpAddress = a.IpAddress,
                Details = a.DetailsJson == null ? null : JsonSerializer.Deserialize<JsonElement>(a.DetailsJson)
            })
            .ToListAsync(ct);

        return new PagedResult<AuditLogResponse>(items, page, pageSize, total);
    }
}
