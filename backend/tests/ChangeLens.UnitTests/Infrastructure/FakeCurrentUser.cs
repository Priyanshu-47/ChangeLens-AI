using ChangeLens.Application.Ports;

namespace ChangeLens.UnitTests.Infrastructure;

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid UserId { get; init; }

    public bool IsGlobalAdmin { get; init; }

    public string? IpAddress => "127.0.0.1";

    public static FakeCurrentUser Standard(Guid? userId = null) =>
        new() { UserId = userId ?? Guid.NewGuid() };

    public static FakeCurrentUser Admin(Guid? userId = null) =>
        new() { UserId = userId ?? Guid.NewGuid(), IsGlobalAdmin = true };
}
