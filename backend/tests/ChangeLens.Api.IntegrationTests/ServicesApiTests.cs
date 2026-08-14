using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;

namespace ChangeLens.Api.IntegrationTests;

[Collection("database")]
public sealed class ServicesApiTests
{
    private readonly TestApi _api;

    public ServicesApiTests(DatabaseFixture fixture) => _api = new TestApi(fixture.Factory);

    [Fact]
    public async Task CreateService_Returns201()
    {
        var (token, _) = await _api.RegisterAsync($"svc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/services", new
        {
            name = "auth-api",
            language = "csharp"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var service = await response.Content.ReadFromJsonAsync<ServiceResponse>();
        Assert.NotNull(service);
        Assert.Equal("auth-api", service.Name);
    }

    [Fact]
    public async Task GetService_NonMember_Returns404()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"svc-{Guid.NewGuid():N}@test.dev");
        var (tokenOther, _) = await _api.RegisterAsync($"svc2-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner);

        Guid serviceId;
        using (var create = _api.NewClient(tokenOwner))
        {
            var created = await create.PostAsJsonAsync($"/api/v1/projects/{projectId}/services", new { name = "auth-api" });
            serviceId = (await created.Content.ReadFromJsonAsync<ServiceResponse>())!.Id;
        }

        using var client = _api.NewClient(tokenOther);
        var response = await client.GetAsync($"/api/v1/services/{serviceId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListServices_ReturnsProjectServices()
    {
        var (token, _) = await _api.RegisterAsync($"svc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using (var create = _api.NewClient(token))
        {
            (await create.PostAsJsonAsync($"/api/v1/projects/{projectId}/services", new { name = "auth-api" }))
                .EnsureSuccessStatusCode();
            (await create.PostAsJsonAsync($"/api/v1/projects/{projectId}/services", new { name = "billing-api" }))
                .EnsureSuccessStatusCode();
        }

        using var client = _api.NewClient(token);
        var response = await client.GetAsync($"/api/v1/projects/{projectId}/services");
        response.EnsureSuccessStatusCode();

        var services = await response.Content.ReadFromJsonAsync<List<ServiceResponse>>();
        Assert.NotNull(services);
        Assert.Equal(2, services.Count);
    }
}
