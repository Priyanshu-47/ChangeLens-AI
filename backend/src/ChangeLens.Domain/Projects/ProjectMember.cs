namespace ChangeLens.Domain.Projects;

/// <summary>
/// Membership linking a user to a project with a project-scoped role.
/// Composite key (ProjectId, UserId); the basis of project-level authorization.
/// </summary>
public sealed class ProjectMember
{
    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public ProjectRole Role { get; set; }

    public Project? Project { get; set; }
}
