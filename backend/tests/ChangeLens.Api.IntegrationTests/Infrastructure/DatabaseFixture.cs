using ChangeLens.Infrastructure.Persistence;
using ChangeLens.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ChangeLens.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shared per-collection fixture. Provisions a real PostgreSQL instance —
/// Testcontainers (needs Docker) by default, or an existing server when
/// CHANGELENS_TEST_CONNECTION_STRING is set (useful on machines without Docker).
/// Applies migrations and seeds roles/demo users before any test runs.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public AppFactory Factory { get; private set; } = null!;

    /// <summary>Connection string of the shared test database (container or external).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("CHANGELENS_TEST_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _container = new PostgreSqlBuilder()
                .WithImage("pgvector/pgvector:pg18")
                .WithDatabase("changelens_test")
                .WithUsername("changelens")
                .WithPassword("changelens_test_password")
                .Build();

            await _container.StartAsync();
            connectionString = _container.GetConnectionString();
        }

        ConnectionString = connectionString;
        Factory = CreateFactory();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SeedData>().EnsureSeededAsync();
    }

    /// <summary>A factory over the same shared database, with optional service overrides.</summary>
    public AppFactory CreateFactory(Action<IServiceCollection>? configureServices = null)
        => new(ConnectionString, configureServices);

    public async Task DisposeAsync()
    {
        Factory?.Dispose();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition("database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
