using ChangeLens.Application.Common;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Repositories;

namespace ChangeLens.Application.Services;

public sealed class RepositoryService(
    IAppDbContext db,
    ProjectAccessService access,
    AuditLogService audit,
    ICurrentUser currentUser)
{
    public async Task<RepositoryResponse> RegisterAsync(
        Guid projectId, CreateRepositoryRequest request, CancellationToken ct)
    {
        var project = await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Write, ct);

        var name = request.Name.Trim();
        var url = request.Url.Trim();
        var language = request.Language.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(language))
        {
            throw new ValidationException("Repository name, URL and language are required.");
        }

        if (!RepositoryUrlValidator.IsValid(url))
        {
            throw new ValidationException(
                "Repository URL must be an https/http URL, a git@ SSH URL, or a relative local path.");
        }

        var repository = new Repository
        {
            ProjectId = projectId,
            Name = name,
            Url = url,
            DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? null : request.DefaultBranch.Trim(),
            Language = language,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Set<Repository>().Add(repository);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.RepositoryRegistered, "Repository", currentUser.UserId, project.Id, repository.Id,
            currentUser.IpAddress, new { repository.Name, repository.Url }, ct);

        return ToResponse(repository);
    }

    public async Task<PagedResult<RepositoryResponse>> ListAsync(Guid projectId, int page, int pageSize, CancellationToken ct)
    {
        await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Read, ct);

        var query = db.Set<Repository>().AsNoTracking().Where(r => r.ProjectId == projectId);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => ToResponse(r))
            .ToListAsync(ct);

        return new PagedResult<RepositoryResponse>(items, page, pageSize, total);
    }

    public async Task<RepositoryResponse> GetAsync(Guid repositoryId, CancellationToken ct)
    {
        var repository = await db.Set<Repository>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct)
            ?? throw new NotFoundException("Repository not found.");

        await access.RequireAsync(
            repository.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Read, ct);

        return ToResponse(repository);
    }

    private static RepositoryResponse ToResponse(Repository r) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        Name = r.Name,
        Url = r.Url,
        DefaultBranch = r.DefaultBranch,
        Language = r.Language,
        CreatedAtUtc = r.CreatedAtUtc
    };
}
