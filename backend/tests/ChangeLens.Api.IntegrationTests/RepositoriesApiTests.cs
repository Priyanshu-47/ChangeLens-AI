using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;

namespace ChangeLens.Api.IntegrationTests;

[Collection("database")]
public sealed class RepositoriesApiTests
{
    private readonly TestApi _api;

    public RepositoriesApiTests(DatabaseFixture fixture) => _api = new TestApi(fixture.Factory);

    [Fact]
    public async Task RegisterRepository_Returns201()
    {
        var (token, _) = await _api.RegisterAsync($"repo-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/repositories", new
        {
            name = "auth-api",
            url = "https://github.com/demo/auth-api.git",
            language = "csharp"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var repo = await response.Content.ReadFromJsonAsync<RepositoryResponse>();
        Assert.NotNull(repo);
        Assert.Equal(projectId, repo.ProjectId);
        Assert.Equal("auth-api", repo.Name);
    }

    [Fact]
    public async Task RegisterRepository_InvalidUrl_Returns400()
    {
        var (token, _) = await _api.RegisterAsync($"repo-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/repositories", new
        {
            name = "auth-api",
            url = "not a repository url",
            language = "csharp"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterRepository_NonMember_Returns404()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"repo-{Guid.NewGuid():N}@test.dev");
        var (tokenOther, _) = await _api.RegisterAsync($"repo2-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner);

        using var client = _api.NewClient(tokenOther);
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/repositories", new
        {
            name = "auth-api",
            url = "https://github.com/demo/auth-api.git",
            language = "csharp"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListRepositories_ReturnsPagedEnvelope()
    {
        var (token, _) = await _api.RegisterAsync($"repo-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using (var create = _api.NewClient(token))
        {
            (await create.PostAsJsonAsync($"/api/v1/projects/{projectId}/repositories", new
            {
                name = "auth-api",
                url = "https://github.com/demo/auth-api.git",
                language = "csharp"
            })).EnsureSuccessStatusCode();
        }

        using var client = _api.NewClient(token);
        var response = await client.GetAsync($"/api/v1/projects/{projectId}/repositories");
        response.EnsureSuccessStatusCode();

        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());

        var body = await response.Content.ReadFromJsonAsync<RepositoryListResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body.Total);
        Assert.Single(body.Items);
    }

    [Fact]
    public async Task GetRepository_NonMember_Returns404()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"repo-{Guid.NewGuid():N}@test.dev");
        var (tokenOther, _) = await _api.RegisterAsync($"repo2-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner);

        Guid repositoryId;
        using (var create = _api.NewClient(tokenOwner))
        {
            var created = await create.PostAsJsonAsync($"/api/v1/projects/{projectId}/repositories", new
            {
                name = "auth-api",
                url = "https://github.com/demo/auth-api.git",
                language = "csharp"
            });
            repositoryId = (await created.Content.ReadFromJsonAsync<RepositoryResponse>())!.Id;
        }

        using var client = _api.NewClient(tokenOther);
        var response = await client.GetAsync($"/api/v1/repositories/{repositoryId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class RepositoryListResponse
    {
        public List<RepositoryResponse> Items { get; init; } = [];
        public int Total { get; init; }
    }
}
