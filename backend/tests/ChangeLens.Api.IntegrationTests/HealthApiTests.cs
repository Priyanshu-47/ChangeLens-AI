using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;

namespace ChangeLens.Api.IntegrationTests;

[Collection("database")]
public sealed class HealthApiTests
{
    private readonly TestApi _api;

    public HealthApiTests(DatabaseFixture fixture) => _api = new TestApi(fixture.Factory);

    [Fact]
    public async Task Liveness_Returns200()
    {
        using var client = _api.NewClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.Equal("changelens-backend", body.Service);
    }

    [Fact]
    public async Task ApiHealth_ReturnsHealthy_WithDatabaseCheck()
    {
        using var client = _api.NewClient();
        var response = await client.GetAsync("/api/v1/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.Contains(body.Checks ?? [], c => c.Name == "database" && c.Status == "Healthy");
    }

    private sealed class HealthBody
    {
        public string Status { get; init; } = string.Empty;

        public string? Service { get; init; }

        public List<HealthCheckEntry>? Checks { get; init; }
    }

    private sealed class HealthCheckEntry
    {
        public string Name { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;
    }
}
