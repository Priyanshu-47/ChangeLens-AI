using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Api.IntegrationTests;

[Collection("database")]
public sealed class ProjectsApiTests
{
    private readonly TestApi _api;

    public ProjectsApiTests(DatabaseFixture fixture) => _api = new TestApi(fixture.Factory);

    [Fact]
    public async Task CreateProject_Returns201_AndCreatorIsOwner()
    {
        var (token, _) = await _api.RegisterAsync($"owner-{Guid.NewGuid():N}@test.dev");
        var name = $"Auth Platform {Guid.NewGuid():N}";

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        Assert.Equal(Application.Common.Slugifier.Slugify(name), project.Slug);
        Assert.Equal(ProjectRole.Owner.ToString(), project.MemberRole);
    }

    [Fact]
    public async Task CreateProject_EmptyName_Returns400()
    {
        var (token, _) = await _api.RegisterAsync($"empty-{Guid.NewGuid():N}@test.dev");

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_AsGlobalViewer_Returns403()
    {
        var token = await _api.LoginAsSeededAsync("viewer@changelens.dev");

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "Sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListProjects_ReturnsOnlyMemberProjects()
    {
        var (tokenA, _) = await _api.RegisterAsync($"list-a-{Guid.NewGuid():N}@test.dev");
        var (tokenB, _) = await _api.RegisterAsync($"list-b-{Guid.NewGuid():N}@test.dev");

        await _api.CreateProjectAsync(tokenA, "Project A");
        await _api.CreateProjectAsync(tokenB, "Project B");

        using var clientA = _api.NewClient(tokenA);
        var response = await clientA.GetAsync("/api/v1/projects");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PagedProjects>();
        Assert.NotNull(body);
        Assert.Equal(1, body.Total);
        Assert.Single(body.Items);
        Assert.Equal("Project A", body.Items[0].Name);
    }

    [Fact]
    public async Task GetProject_NonMember_Returns404()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"iso-{Guid.NewGuid():N}@test.dev");
        var (tokenOther, _) = await _api.RegisterAsync($"iso2-{Guid.NewGuid():N}@test.dev");

        var projectId = await _api.CreateProjectAsync(tokenOwner, "Isolated");

        using var client = _api.NewClient(tokenOther);
        var response = await client.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProject_ByOwner_Returns200()
    {
        var (token, _) = await _api.RegisterAsync($"upd-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token, "Original Name");

        var renamed = $"Renamed {Guid.NewGuid():N}";

        using var client = _api.NewClient(token);
        var response = await client.PatchAsJsonAsync($"/api/v1/projects/{projectId}", new { name = renamed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(updated);
        Assert.Equal(renamed, updated.Name);
        Assert.Equal(Application.Common.Slugifier.Slugify(renamed), updated.Slug);
    }

    [Fact]
    public async Task UpdateProject_ByViewerMember_Returns403()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"upd-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner, "Shared");

        using (var add = _api.NewClient(tokenOwner))
        {
            var addResponse = await add.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
                new { email = "viewer@changelens.dev", role = ProjectRole.Viewer });
            addResponse.EnsureSuccessStatusCode();
        }

        var viewerToken = await _api.LoginAsSeededAsync("viewer@changelens.dev");

        using var client = _api.NewClient(viewerToken);
        var response = await client.PatchAsJsonAsync($"/api/v1/projects/{projectId}", new { name = "Hijack" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_ThenMemberCanRead()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"mem-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner, "Team Project");

        var memberEmail = $"member-{Guid.NewGuid():N}@test.dev";
        var (memberToken, _) = await _api.RegisterAsync(memberEmail);

        using (var add = _api.NewClient(tokenOwner))
        {
            var addResponse = await add.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
                new { email = memberEmail, role = ProjectRole.Engineer });
            addResponse.EnsureSuccessStatusCode();
        }

        using var client = _api.NewClient(memberToken);
        var response = await client.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_UnknownEmail_Returns404()
    {
        var (token, _) = await _api.RegisterAsync($"mem-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token, "Team Project");

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
            new { email = "nobody@test.dev", role = ProjectRole.Engineer });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_LastOwner_Returns409()
    {
        var (token, userId) = await _api.RegisterAsync($"own-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token, "Solo Project");

        using var client = _api.NewClient(token);
        var response = await client.DeleteAsync($"/api/v1/projects/{projectId}/members/{userId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_RevokesReadAccess()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"rev-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner, "Revocable");

        var memberEmail = $"revoked-{Guid.NewGuid():N}@test.dev";
        var (memberToken, memberId) = await _api.RegisterAsync(memberEmail);

        using (var add = _api.NewClient(tokenOwner))
        {
            (await add.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
                new { email = memberEmail, role = ProjectRole.Engineer })).EnsureSuccessStatusCode();
        }

        using (var remove = _api.NewClient(tokenOwner))
        {
            var removeResponse = await remove.DeleteAsync($"/api/v1/projects/{projectId}/members/{memberId}");
            Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);
        }

        using var client = _api.NewClient(memberToken);
        var response = await client.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class PagedProjects
    {
        public List<ProjectResponse> Items { get; init; } = [];
        public int Total { get; init; }
    }
}
