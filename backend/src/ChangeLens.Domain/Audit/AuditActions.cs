namespace ChangeLens.Domain.Audit;

/// <summary>Canonical audit action names. Stored as strings so the vocabulary can grow without migrations.</summary>
public static class AuditActions
{
    public const string UserRegistered = "UserRegistered";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";

    public const string ProjectCreated = "ProjectCreated";
    public const string ProjectUpdated = "ProjectUpdated";
    public const string MemberAdded = "MemberAdded";
    public const string MemberRemoved = "MemberRemoved";
    public const string MemberRoleChanged = "MemberRoleChanged";

    public const string RepositoryRegistered = "RepositoryRegistered";
    public const string ServiceCreated = "ServiceCreated";

    public const string IncidentCreated = "IncidentCreated";
    public const string IncidentUpdated = "IncidentUpdated";
    public const string IncidentEventAdded = "IncidentEventAdded";
}
