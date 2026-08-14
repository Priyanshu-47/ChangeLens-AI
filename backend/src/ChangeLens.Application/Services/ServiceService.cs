using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Services;

namespace ChangeLens.Application.Services;

public sealed class ServiceService(
    IAppDbContext db,
    ProjectAccessService access,
    AuditLogService audit,
    ICurrentUser currentUser)
{
    public async Task<ServiceResponse> CreateAsync(Guid projectId, CreateServiceRequest request, CancellationToken ct)
    {
        var project = await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Write, ct);

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Service name is required.");
        }

        var service = new Service
        {
            ProjectId = projectId,
            Name = name,
            Language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language.Trim(),
            RootPath = string.IsNullOrWhiteSpace(request.RootPath) ? null : request.RootPath.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Set<Service>().Add(service);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.ServiceCreated, "Service", currentUser.UserId, project.Id, service.Id,
            currentUser.IpAddress, new { service.Name }, ct);

        return ToResponse(service);
    }

    public async Task<ServiceResponse> GetAsync(Guid serviceId, CancellationToken ct)
    {
        var service = await db.Set<Service>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId, ct)
            ?? throw new NotFoundException("Service not found.");

        await access.RequireAsync(
            service.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Read, ct);

        return ToResponse(service);
    }

    public async Task<List<ServiceResponse>> ListAsync(Guid projectId, CancellationToken ct)
    {
        await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Read, ct);

        return await db.Set<Service>().AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Name)
            .Select(s => ToResponse(s))
            .ToListAsync(ct);
    }

    private static ServiceResponse ToResponse(Service s) => new()
    {
        Id = s.Id,
        ProjectId = s.ProjectId,
        Name = s.Name,
        Language = s.Language,
        RootPath = s.RootPath,
        CreatedAtUtc = s.CreatedAtUtc
    };
}
