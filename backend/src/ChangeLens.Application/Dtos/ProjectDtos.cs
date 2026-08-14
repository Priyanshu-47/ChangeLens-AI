using System.ComponentModel.DataAnnotations;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Application.Dtos;

public sealed class CreateProjectRequest
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }
}

public sealed class UpdateProjectRequest
{
    [MaxLength(120)]
    public string? Name { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }
}

public sealed class ProjectResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }

    /// <summary>Caller's project role ("Owner", "Admin", "Engineer", "Viewer"; "Admin" for global admins).</summary>
    public string MemberRole { get; init; } = string.Empty;
}

public sealed class AddMemberRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(ProjectRole))]
    public ProjectRole Role { get; set; }
}

public sealed class MemberResponse
{
    public Guid UserId { get; init; }

    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;
}
