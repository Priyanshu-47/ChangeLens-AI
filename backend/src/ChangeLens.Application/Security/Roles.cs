namespace ChangeLens.Application.Security;

/// <summary>Global application roles (Identity roles), distinct from project-scoped roles.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Engineer = "Engineer";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, Engineer, Viewer];
}
