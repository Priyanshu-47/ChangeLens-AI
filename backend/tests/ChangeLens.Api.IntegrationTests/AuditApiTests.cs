using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;
using ChangeLens.Domain.Projects;

namespace ChangeLens.Api.IntegrationTests;

[Collection("database")]
public sealed class AuditApiTests
{
    private readonly TestApi _api;

    public AuditApiTests(DatabaseFixture fixture) => _api = new TestApi(fixture.Factory);

    [Fact]
    public async Task AuditLogs_RecordProjectAndIncidentMutations()
    {
        var (token, _) = await _api.RegisterAsync($"audit-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token, "Audited Project");

        using (var create = _api.NewClient(token))
        {
            (await create.PostAsJsonAsync("/api/v1/incidents", new
            {
                projectId,
                title = "Audited incident"
            })).EnsureSuccessStatusCode();
        }

        using var client = _api.NewClient(token);
        var response = await client.GetAsync($"/api/v1/audit-logs?projectId={projectId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        Assert.NotNull(body);

        var actions = body.Items.Select(i => i.Action).ToList();
        Assert.Contains("ProjectCreated", actions);
        Assert.Contains("IncidentCreated", actions);
    }

    [Fact]
    public async Task AuditLogs_ViewerMember_Returns403()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"audit-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner, "Shared Audit");

        using (var add = _api.NewClient(tokenOwner))
        {
            (await add.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
                new { email = "viewer@changelens.dev", role = ProjectRole.Viewer })).EnsureSuccessStatusCode();
        }

        var viewerToken = await _api.LoginAsSeededAsync("viewer@changelens.dev");

        using var client = _api.NewClient(viewerToken);
        var response = await client.GetAsync($"/api/v1/audit-logs?projectId={projectId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class AuditListResponse
    {
        public List<AuditLogResponse> Items { get; init; } = [];
        public int Total { get; init; }
    }
}
