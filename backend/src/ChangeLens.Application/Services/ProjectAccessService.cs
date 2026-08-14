using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Application.Services;

public enum ProjectPermission
{
    /// <summary>Any project member (or global admin).</summary>
    Read,

    /// <summary>Owner, Admin or Engineer project role (or global admin).</summary>
    Write,

    /// <summary>Owner or Admin project role (or global admin).</summary>
    Manage
}

/// <summary>
/// Central project-level authorization. Non-members see 404 (project existence is not
/// revealed); members with an insufficient role get 403. Global admins bypass membership.
/// This is the first enforcement layer — every query is additionally filtered by
/// project id at the data layer (defense in depth, see security-model.md §3).
/// </summary>
public sealed class ProjectAccessService(IAppDbContext db)
{
    /// <summary>
    /// Returns the project if the caller may perform <paramref name="permission"/> on it.
    /// Throws <see cref="NotFoundException"/> for missing/invisible projects and
    /// <see cref="ForbiddenAccessException"/> for recognized members lacking the role.
    /// </summary>
    public async Task<Project> RequireAsync(
        Guid projectId,
        Guid userId,
        bool isGlobalAdmin,
        ProjectPermission permission,
        CancellationToken ct)
    {
        var project = await db.Set<Project>()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
        {
            throw new NotFoundException("Project not found.");
        }

        if (isGlobalAdmin)
        {
            return project;
        }

        var member = await db.Set<ProjectMember>()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

        if (member is null)
        {
            throw new NotFoundException("Project not found.");
        }

        var allowed = permission switch
        {
            ProjectPermission.Read => true,
            ProjectPermission.Write => member.Role is ProjectRole.Owner or ProjectRole.Admin or ProjectRole.Engineer,
            ProjectPermission.Manage => member.Role is ProjectRole.Owner or ProjectRole.Admin,
            _ => false
        };

        if (!allowed)
        {
            throw new ForbiddenAccessException(
                $"Project role '{member.Role}' does not permit this operation.");
        }

        return project;
    }
}
