namespace ChangeLens.Application.Ports;

/// <summary>
/// The authenticated caller of the current request. Implemented by an HTTP adapter
/// (claims principal + connection info); faked in unit tests.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    /// <summary>True when the caller holds the global Admin role (bypasses project membership for reads).</summary>
    bool IsGlobalAdmin { get; }

    string? IpAddress { get; }
}
