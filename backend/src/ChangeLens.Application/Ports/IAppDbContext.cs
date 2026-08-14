using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ChangeLens.Application.Ports;

/// <summary>
/// Minimal persistence port implemented by <c>AppDbContext</c> (Infrastructure).
/// Keeps the dependency direction clean: Application depends on this interface,
/// not on the EF provider. Unit tests provide an in-memory implementation.
/// </summary>
public interface IAppDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
