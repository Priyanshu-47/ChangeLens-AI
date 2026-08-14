using ChangeLens.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChangeLens.UnitTests.Infrastructure;

/// <summary>
/// Wires real application services against an in-memory context and a fake current
/// user. Services are constructed on each access (they are stateless wrappers over
/// the shared context) so a test can swap <see cref="User"/> mid-test to exercise
/// different callers.
/// </summary>
public abstract class ServiceTestBase
{
    protected InMemoryAppDbContext Context { get; } = InMemoryAppDbContext.CreateNew();

    protected FakeCurrentUser User { get; set; } = FakeCurrentUser.Standard();

    protected ProjectAccessService Access => new(Context);

    protected AuditLogService Audit => new(Context, NullLogger<AuditLogService>.Instance);

    protected ProjectService Projects => new(Context, Access, Audit, User);

    protected RepositoryService Repositories => new(Context, Access, Audit, User);

    protected ServiceService Services => new(Context, Access, Audit, User);

    protected IncidentService Incidents => new(Context, Access, Audit, User);

    /// <summary>Creates a project as the current user (Owner) and returns its id.</summary>
    protected async Task<Guid> CreateProjectAsync(string name = "Demo Project")
    {
        var project = await Projects.CreateAsync(
            new ChangeLens.Application.Dtos.CreateProjectRequest { Name = name },
            CancellationToken.None);

        return project.Id;
    }
}
