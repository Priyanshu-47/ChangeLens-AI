using ChangeLens.Application.Common;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Security;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Application.Services;

public sealed class ProjectService(
    IAppDbContext db,
    ProjectAccessService access,
    AuditLogService audit,
    ICurrentUser currentUser)
{
    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Project name is required.");
        }

        var project = new Project
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Slug = await EnsureUniqueSlugAsync(Slugifier.Slugify(name), ct),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Set<Project>().Add(project);
        db.Set<ProjectMember>().Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = currentUser.UserId,
            Role = ProjectRole.Owner
        });

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.ProjectCreated, "Project", currentUser.UserId, project.Id, project.Id,
            currentUser.IpAddress, new { project.Name, project.Slug }, ct);

        return ToResponse(project, ProjectRole.Owner);
    }

    public async Task<PagedResult<ProjectResponse>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = currentUser.IsGlobalAdmin
            ? db.Set<Project>().AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Select(p => new { Project = p, Role = ProjectRole.Admin })
            : db.Set<ProjectMember>().AsNoTracking()
                .Where(m => m.UserId == currentUser.UserId)
                .Select(m => new { Project = m.Project!, Role = m.Role });

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.Project.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToResponse(x.Project, x.Role))
            .ToListAsync(ct);

        return new PagedResult<ProjectResponse>(items, page, pageSize, total);
    }

    public async Task<ProjectResponse> GetAsync(Guid projectId, CancellationToken ct)
    {
        var project = await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Read, ct);

        var role = currentUser.IsGlobalAdmin
            ? ProjectRole.Admin
            : (await db.Set<ProjectMember>().AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == currentUser.UserId, ct))?.Role
              ?? ProjectRole.Viewer;

        return ToResponse(project, role);
    }

    public async Task<ProjectResponse> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken ct)
    {
        var project = await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Manage, ct);

        var hasChanges = false;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var newName = request.Name.Trim();
            if (newName != project.Name)
            {
                project.Name = newName;
                project.Slug = await EnsureUniqueSlugAsync(Slugifier.Slugify(newName), ct, excludeId: project.Id);
                hasChanges = true;
            }
        }

        if (request.Description is not null)
        {
            var newDescription = request.Description.Trim();
            if (newDescription != project.Description)
            {
                project.Description = string.IsNullOrEmpty(newDescription) ? null : newDescription;
                hasChanges = true;
            }
        }

        if (!hasChanges)
        {
            throw new ValidationException("No changes to apply.");
        }

        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.ProjectUpdated, "Project", currentUser.UserId, project.Id, project.Id,
            currentUser.IpAddress, null, ct);

        var role = currentUser.IsGlobalAdmin
            ? ProjectRole.Admin
            : (await db.Set<ProjectMember>().AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == currentUser.UserId, ct))?.Role
              ?? ProjectRole.Viewer;

        return ToResponse(project, role);
    }

    public async Task<MemberResponse> AddMemberAsync(
        Guid projectId, Guid userId, string email, string displayName, ProjectRole role, CancellationToken ct)
    {
        var project = await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Manage, ct);

        var existing = await db.Set<ProjectMember>()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

        if (existing is not null)
        {
            if (existing.Role == role)
            {
                return new MemberResponse { UserId = userId, Email = email, DisplayName = displayName, Role = role.ToString() };
            }

            existing.Role = role;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(
                AuditActions.MemberRoleChanged, "ProjectMember", currentUser.UserId, project.Id,
                null, currentUser.IpAddress, new { userId, role }, ct);

            return new MemberResponse { UserId = userId, Email = email, DisplayName = displayName, Role = role.ToString() };
        }

        db.Set<ProjectMember>().Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role
        });
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.MemberAdded, "ProjectMember", currentUser.UserId, project.Id,
            null, currentUser.IpAddress, new { userId, role }, ct);

        return new MemberResponse { UserId = userId, Email = email, DisplayName = displayName, Role = role.ToString() };
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await access.RequireAsync(
            projectId, currentUser.UserId, currentUser.IsGlobalAdmin, ProjectPermission.Manage, ct);

        var member = await db.Set<ProjectMember>()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct)
            ?? throw new NotFoundException("Project member not found.");

        if (member.Role == ProjectRole.Owner)
        {
            var ownerCount = await db.Set<ProjectMember>()
                .CountAsync(m => m.ProjectId == projectId && m.Role == ProjectRole.Owner, ct);

            if (ownerCount <= 1)
            {
                throw new ConflictException("Cannot remove the last project owner.");
            }
        }

        db.Set<ProjectMember>().Remove(member);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.MemberRemoved, "ProjectMember", currentUser.UserId, project.Id,
            null, currentUser.IpAddress, new { userId }, ct);
    }

    public async Task<List<ProjectMembershipResponse>> GetMembershipsAsync(Guid userId, CancellationToken ct)
    {
        return await db.Set<ProjectMember>().AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new ProjectMembershipResponse
            {
                ProjectId = m.ProjectId,
                ProjectName = m.Project!.Name,
                Role = m.Role.ToString()
            })
            .OrderBy(m => m.ProjectName)
            .ToListAsync(ct);
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, CancellationToken ct, Guid? excludeId = null)
    {
        var slug = baseSlug;
        var suffix = 2;

        while (await db.Set<Project>().AnyAsync(p => p.Slug == slug && p.Id != excludeId, ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static ProjectResponse ToResponse(Project p, ProjectRole role) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Slug = p.Slug,
        Description = p.Description,
        CreatedAtUtc = p.CreatedAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc,
        MemberRole = role.ToString()
    };
}
