using ChangeLens.Application.Ports;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Incidents;
using ChangeLens.Domain.Projects;
using ChangeLens.Domain.Repositories;
using ChangeLens.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace ChangeLens.UnitTests.Infrastructure;

/// <summary>
/// In-memory implementation of <see cref="IAppDbContext"/> for service unit tests.
/// Exposes the same entity sets as the relational context and mirrors its query
/// filters so soft-delete behavior is exercised identically. Note: the in-memory
/// provider does not enforce unique constraints or relationships — services
/// enforce those invariants in code.
/// </summary>
public sealed class InMemoryAppDbContext(DbContextOptions<InMemoryAppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<Repository> Repositories => Set<Repository>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentEvent> IncidentEvents => Set<IncidentEvent>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Project>().HasQueryFilter(p => !p.IsDeleted);
        builder.Entity<Repository>().HasQueryFilter(r => !r.IsDeleted);
        builder.Entity<ProjectMember>().HasKey(m => new { m.ProjectId, m.UserId });
    }

    public static InMemoryAppDbContext CreateNew()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase($"changelens-tests-{Guid.NewGuid():N}")
            .Options;

        return new InMemoryAppDbContext(options);
    }
}
