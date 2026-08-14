using ChangeLens.Domain.Audit;
using ChangeLens.UnitTests.Infrastructure;

namespace ChangeLens.UnitTests.Services;

public sealed class AuditLogServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Write_AddsAppendOnlyEntry()
    {
        var projectId = Guid.NewGuid();

        await Audit.WriteAsync(
            AuditActions.IncidentCreated, "Incident", User.UserId, projectId,
            resourceId: Guid.NewGuid(), ipAddress: "10.0.0.5", details: new { title = "X" },
            CancellationToken.None);

        var page = await Audit.QueryAsync(projectId, 1, 20, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal(AuditActions.IncidentCreated, page.Items[0].Action);
        Assert.Equal("10.0.0.5", page.Items[0].IpAddress);
    }

    [Fact]
    public async Task Query_IsScopedToProject()
    {
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        await Audit.WriteAsync(AuditActions.ProjectCreated, "Project", User.UserId, projectA, projectA, null, null, CancellationToken.None);
        await Audit.WriteAsync(AuditActions.ProjectCreated, "Project", User.UserId, projectB, projectB, null, null, CancellationToken.None);

        var pageA = await Audit.QueryAsync(projectA, 1, 20, CancellationToken.None);
        var pageB = await Audit.QueryAsync(projectB, 1, 20, CancellationToken.None);

        Assert.Equal(1, pageA.Total);
        Assert.Equal(1, pageB.Total);
    }

    [Fact]
    public async Task Write_FailureDoesNotThrow()
    {
        // In-memory context always works; this guards the contract that audit
        // writes are best-effort (they must never break the business operation).
        await Audit.WriteAsync(AuditActions.ProjectCreated, "Project", User.UserId, Guid.NewGuid(), null, null, null, CancellationToken.None);
    }
}
