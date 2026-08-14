using System.Text.Json;
using ChangeLens.Application.Common;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Incidents;
using ChangeLens.Domain.Services;

namespace ChangeLens.Application.Services;

public sealed class IncidentService(
    IAppDbContext db,
    ProjectAccessService access,
    AuditLogService audit,
    ICurrentUser currentUser)
{
    public async Task<IncidentResponse> CreateAsync(CreateIncidentRequest request, CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty)
        {
            throw new ValidationException("ProjectId is required.");
        }

        var project = await access.RequireAsync(
            request.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Write, ct);

        await EnsureServiceBelongsToProjectAsync(request.ProjectId, request.AffectedServiceId, ct);

        var incident = new Incident
        {
            ProjectId = request.ProjectId,
            Title = request.Title.Trim(),
            Severity = request.Severity,
            Status = request.Status,
            Classification = Trimmed(request.Classification),
            AffectedServiceId = request.AffectedServiceId,
            Environment = Trimmed(request.Environment),
            StartedAtUtc = request.StartedAtUtc ?? DateTime.UtcNow,
            DetectedAtUtc = request.DetectedAtUtc,
            Summary = Trimmed(request.Summary),
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var eventRequest in request.Events)
        {
            incident.Events.Add(new IncidentEvent
            {
                OccurredAtUtc = eventRequest.OccurredAtUtc ?? DateTime.UtcNow,
                Type = eventRequest.Type,
                Source = Trimmed(eventRequest.Source),
                Message = Trimmed(eventRequest.Message),
                RawDataJson = eventRequest.RawData?.GetRawText()
            });
        }

        db.Set<Incident>().Add(incident);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.IncidentCreated, "Incident", currentUser.UserId, incident.ProjectId, incident.Id,
            currentUser.IpAddress, new { incident.Title, incident.Severity }, ct);

        return await ToDetailResponseAsync(incident.Id, ct);
    }

    public async Task<PagedResult<IncidentListItemResponse>> ListAsync(
        Guid projectId,
        IncidentStatus? status,
        IncidentSeverity? severity,
        Guid? affectedServiceId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Read, ct);

        var query = db.Set<Incident>().AsNoTracking().Where(i => i.ProjectId == projectId);

        if (status is not null)
        {
            query = query.Where(i => i.Status == status);
        }

        if (severity is not null)
        {
            query = query.Where(i => i.Severity == severity);
        }

        if (affectedServiceId is not null)
        {
            query = query.Where(i => i.AffectedServiceId == affectedServiceId);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(i => i.StartedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => ToListItemResponse(i))
            .ToListAsync(ct);

        return new PagedResult<IncidentListItemResponse>(items, page, pageSize, total);
    }

    public async Task<IncidentResponse> GetAsync(Guid incidentId, CancellationToken ct)
    {
        var incident = await db.Set<Incident>().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new NotFoundException("Incident not found.");

        await access.RequireAsync(
            incident.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Read, ct);

        return await ToDetailResponseAsync(incidentId, ct);
    }

    public async Task<IncidentResponse> UpdateAsync(Guid incidentId, UpdateIncidentRequest request, CancellationToken ct)
    {
        var incident = await db.Set<Incident>()
            .FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new NotFoundException("Incident not found.");

        await access.RequireAsync(
            incident.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Write, ct);

        var hasChanges = false;

        if (!string.IsNullOrWhiteSpace(request.Title) && request.Title.Trim() != incident.Title)
        {
            incident.Title = request.Title.Trim();
            hasChanges = true;
        }

        if (request.Severity is not null && request.Severity != incident.Severity)
        {
            incident.Severity = request.Severity.Value;
            hasChanges = true;
        }

        if (request.Status is not null && request.Status != incident.Status)
        {
            incident.Status = request.Status.Value;
            hasChanges = true;
        }

        if (request.Classification is not null && Trimmed(request.Classification) != incident.Classification)
        {
            incident.Classification = Trimmed(request.Classification);
            hasChanges = true;
        }

        if (request.Environment is not null && Trimmed(request.Environment) != incident.Environment)
        {
            incident.Environment = Trimmed(request.Environment);
            hasChanges = true;
        }

        if (request.Summary is not null && Trimmed(request.Summary) != incident.Summary)
        {
            incident.Summary = Trimmed(request.Summary);
            hasChanges = true;
        }

        if (!hasChanges)
        {
            throw new ValidationException("No changes to apply.");
        }

        incident.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.IncidentUpdated, "Incident", currentUser.UserId, incident.ProjectId, incident.Id,
            currentUser.IpAddress, null, ct);

        return await ToDetailResponseAsync(incidentId, ct);
    }

    public async Task<IncidentEventResponse> AddEventAsync(Guid incidentId, CreateIncidentEventRequest request, CancellationToken ct)
    {
        var incident = await db.Set<Incident>()
            .FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new NotFoundException("Incident not found.");

        await access.RequireAsync(
            incident.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Write, ct);

        var incidentEvent = new IncidentEvent
        {
            IncidentId = incidentId,
            OccurredAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow,
            Type = request.Type,
            Source = Trimmed(request.Source),
            Message = Trimmed(request.Message),
            RawDataJson = request.RawData?.GetRawText()
        };

        db.Set<IncidentEvent>().Add(incidentEvent);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.IncidentEventAdded, "IncidentEvent", currentUser.UserId, incident.ProjectId,
            incidentEvent.Id, currentUser.IpAddress, null, ct);

        return ToEventResponse(incidentEvent);
    }

    private async Task EnsureServiceBelongsToProjectAsync(Guid projectId, Guid? serviceId, CancellationToken ct)
    {
        if (serviceId is null)
        {
            return;
        }

        var exists = await db.Set<Service>().AnyAsync(s => s.Id == serviceId && s.ProjectId == projectId, ct);
        if (!exists)
        {
            throw new ValidationException("Affected service does not belong to the project.");
        }
    }

    private async Task<IncidentResponse> ToDetailResponseAsync(Guid incidentId, CancellationToken ct)
    {
        var incident = await db.Set<Incident>().AsNoTracking()
            .Include(i => i.Events)
            .FirstAsync(i => i.Id == incidentId, ct);

        return new IncidentResponse
        {
            Id = incident.Id,
            ProjectId = incident.ProjectId,
            Title = incident.Title,
            Severity = incident.Severity,
            Status = incident.Status,
            Classification = incident.Classification,
            AffectedServiceId = incident.AffectedServiceId,
            Environment = incident.Environment,
            StartedAtUtc = incident.StartedAtUtc,
            DetectedAtUtc = incident.DetectedAtUtc,
            Summary = incident.Summary,
            CreatedAtUtc = incident.CreatedAtUtc,
            Events = incident.Events
                .OrderBy(e => e.OccurredAtUtc)
                .Select(ToEventResponse)
                .ToList()
        };
    }

    private static IncidentListItemResponse ToListItemResponse(Incident i) => new()
    {
        Id = i.Id,
        ProjectId = i.ProjectId,
        Title = i.Title,
        Severity = i.Severity,
        Status = i.Status,
        Classification = i.Classification,
        AffectedServiceId = i.AffectedServiceId,
        Environment = i.Environment,
        StartedAtUtc = i.StartedAtUtc,
        DetectedAtUtc = i.DetectedAtUtc,
        Summary = i.Summary,
        CreatedAtUtc = i.CreatedAtUtc
    };

    private static IncidentEventResponse ToEventResponse(IncidentEvent e) => new()
    {
        Id = e.Id,
        OccurredAtUtc = e.OccurredAtUtc,
        Type = e.Type,
        Source = e.Source,
        Message = e.Message,
        RawData = e.RawDataJson is null ? null : JsonSerializer.Deserialize<JsonElement>(e.RawDataJson)
    };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
