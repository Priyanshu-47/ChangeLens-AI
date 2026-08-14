using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChangeLens.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Test host for the ChangeLens API. Configuration is overridden via environment
/// variables (which outrank appsettings JSON in the default configuration order),
/// so the app under test connects to the test database and uses test JWT settings.
/// Seeding is performed by the fixture after migrations are applied (the app itself
/// never seeds in tests).
/// </summary>
public sealed class AppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "changelens-test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "changelens-test-client");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-only-signing-key-0123456789abcdef0123456789abcdef");
        Environment.SetEnvironmentVariable("Jwt__ExpiryMinutes", "60");
    }
}
