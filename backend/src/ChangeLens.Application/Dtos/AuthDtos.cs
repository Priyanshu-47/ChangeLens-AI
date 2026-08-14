using System.ComponentModel.DataAnnotations;

namespace ChangeLens.Application.Dtos;

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? DisplayName { get; set; }
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}

public sealed class UserResponse
{
    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed class AuthResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public int ExpiresInSeconds { get; init; }

    public string TokenType { get; init; } = "Bearer";

    public UserResponse User { get; init; } = null!;
}

public sealed class ProjectMembershipResponse
{
    public Guid ProjectId { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;
}

public sealed class MeResponse
{
    public UserResponse User { get; init; } = null!;

    public IReadOnlyList<ProjectMembershipResponse> Memberships { get; init; } = [];
}
