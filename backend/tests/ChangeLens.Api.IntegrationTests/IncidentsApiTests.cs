using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;
using ChangeLens.Domain.Incidents;

namespace ChangeLens.Api.IntegrationTests;

[Collection("database")]
public sealed class IncidentsApiTests
{
    private readonly TestApi _api;

    public IncidentsApiTests(DatabaseFixture fixture) => _api = new TestApi(fixture.Factory);

    [Fact]
    public async Task CreateIncident_Returns201_WithEvents()
    {
        var (token, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            projectId,
            title = "Token refresh failures",
            severity = IncidentSeverity.Sev2,
            events = new[]
            {
                new { type = IncidentEventType.Log, source = "auth-api", message = "invalid signature" },
                new { type = IncidentEventType.Deployment, source = "ci", message = "deployed v2.4.1" }
            }
        }, TestJson.Options);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var incident = await response.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options);
        Assert.NotNull(incident);
        Assert.Equal(IncidentSeverity.Sev2, incident.Severity);
        Assert.Equal(2, incident.Events.Count);
        Assert.Equal(IncidentEventType.Log, incident.Events[0].Type);
        Assert.Equal(IncidentEventType.Deployment, incident.Events[1].Type);
    }

    [Fact]
    public async Task CreateIncident_MissingProjectId_Returns400()
    {
        var (token, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/incidents", new { title = "No project" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateIncident_InvalidSeverity_Returns400()
    {
        var (token, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            projectId,
            title = "Bad severity",
            severity = "Sev99"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateIncident_AffectedServiceFromOtherProject_Returns400()
    {
        var (tokenA, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var (tokenB, _) = await _api.RegisterAsync($"inc2-{Guid.NewGuid():N}@test.dev");
        var projectA = await _api.CreateProjectAsync(tokenA, "Project A");
        var projectB = await _api.CreateProjectAsync(tokenB, "Project B");

        Guid serviceB;
        using (var create = _api.NewClient(tokenB))
        {
            var created = await create.PostAsJsonAsync($"/api/v1/projects/{projectB}/services", new { name = "billing-api" });
            serviceB = (await created.Content.ReadFromJsonAsync<ServiceResponse>(TestJson.Options))!.Id;
        }

        using var client = _api.NewClient(tokenA);
        var response = await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            projectId = projectA,
            title = "Wrong service",
            affectedServiceId = serviceB
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetIncident_ReturnsDetailWithEvents()
    {
        var (token, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        Guid incidentId;
        using (var create = _api.NewClient(token))
        {
            var created = await create.PostAsJsonAsync("/api/v1/incidents", new
            {
                projectId,
                title = "Detail me",
                events = new[] { new { type = IncidentEventType.Error, message = "boom" } }
            }, TestJson.Options);
            incidentId = (await created.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options))!.Id;
        }

        using var client = _api.NewClient(token);
        var response = await client.GetAsync($"/api/v1/incidents/{incidentId}");
        response.EnsureSuccessStatusCode();

        var incident = await response.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options);
        Assert.NotNull(incident);
        Assert.Equal("Detail me", incident.Title);
        Assert.Single(incident.Events);
        Assert.Equal("boom", incident.Events[0].Message);
        Assert.Equal(IncidentEventType.Error, incident.Events[0].Type);
    }

    [Fact]
    public async Task GetIncident_NonMember_Returns404()
    {
        var (tokenOwner, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var (tokenOther, _) = await _api.RegisterAsync($"inc2-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(tokenOwner);

        Guid incidentId;
        using (var create = _api.NewClient(tokenOwner))
        {
            var created = await create.PostAsJsonAsync("/api/v1/incidents", new { projectId, title = "Secret" }, TestJson.Options);
            incidentId = (await created.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options))!.Id;
        }

        using var client = _api.NewClient(tokenOther);
        var response = await client.GetAsync($"/api/v1/incidents/{incidentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListIncidents_FiltersByStatus()
    {
        var (token, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        using (var create = _api.NewClient(token))
        {
            (await create.PostAsJsonAsync("/api/v1/incidents", new { projectId, title = "Open incident" }, TestJson.Options))
                .EnsureSuccessStatusCode();
            (await create.PostAsJsonAsync("/api/v1/incidents", new
            {
                projectId,
                title = "Resolved incident",
                status = IncidentStatus.Resolved
            }, TestJson.Options)).EnsureSuccessStatusCode();
        }

        using var client = _api.NewClient(token);
        var response = await client.GetAsync($"/api/v1/incidents?projectId={projectId}&status=Resolved");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<IncidentListResponse>(TestJson.Options);
        Assert.NotNull(body);
        Assert.Equal(1, body.Total);
        Assert.Equal("Resolved incident", body.Items[0].Title);
        Assert.Equal(IncidentStatus.Resolved, body.Items[0].Status);
    }

    [Fact]
    public async Task AddEvent_Returns201()
    {
        var (token, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        Guid incidentId;
        using (var create = _api.NewClient(token))
        {
            var created = await create.PostAsJsonAsync("/api/v1/incidents", new { projectId, title = "Timeline" }, TestJson.Options);
            incidentId = (await created.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options))!.Id;
        }

        using var client = _api.NewClient(token);
        var response = await client.PostAsJsonAsync($"/api/v1/incidents/{incidentId}/events", new
        {
            type = IncidentEventType.Metric,
            message = "error_rate 0.42"
        }, TestJson.Options);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdEvent = await response.Content.ReadFromJsonAsync<IncidentEventResponse>(TestJson.Options);
        Assert.NotNull(createdEvent);
        Assert.Equal(IncidentEventType.Metric, createdEvent.Type);
    }

    [Fact]
    public async Task UpdateIncident_Status_Returns200()
    {
        var (token, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var projectId = await _api.CreateProjectAsync(token);

        Guid incidentId;
        using (var create = _api.NewClient(token))
        {
            var created = await create.PostAsJsonAsync("/api/v1/incidents", new { projectId, title = "Fixable" }, TestJson.Options);
            incidentId = (await created.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options))!.Id;
        }

        using var client = _api.NewClient(token);
        var response = await client.PatchAsJsonAsync($"/api/v1/incidents/{incidentId}", new
        {
            status = IncidentStatus.Resolved,
            classification = "DeploymentRegression"
        }, TestJson.Options);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options);
        Assert.NotNull(updated);
        Assert.Equal(IncidentStatus.Resolved, updated.Status);
        Assert.Equal("DeploymentRegression", updated.Classification);
    }

    [Fact]
    public async Task Incident_CrossProjectIsolation()
    {
        var (tokenA, _) = await _api.RegisterAsync($"inc-{Guid.NewGuid():N}@test.dev");
        var (tokenB, _) = await _api.RegisterAsync($"inc2-{Guid.NewGuid():N}@test.dev");
        var projectA = await _api.CreateProjectAsync(tokenA, "Project A");

        Guid incidentId;
        using (var create = _api.NewClient(tokenA))
        {
            var created = await create.PostAsJsonAsync("/api/v1/incidents", new { projectId = projectA, title = "A's secret" }, TestJson.Options);
            incidentId = (await created.Content.ReadFromJsonAsync<IncidentResponse>(TestJson.Options))!.Id;
        }

        using var clientB = _api.NewClient(tokenB);
        var getResponse = await clientB.GetAsync($"/api/v1/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await clientB.GetAsync($"/api/v1/incidents?projectId={projectA}");
        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);
    }

    private sealed class IncidentListResponse
    {
        public List<IncidentListItemResponse> Items { get; init; } = [];
        public int Total { get; init; }
    }
}
