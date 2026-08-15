using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace ChangeLens.Api.IntegrationTests;

/// <summary>
/// Phase 5 async incident workflow against real PostgreSQL (brief §36–37):
///
///   POST /incidents/{id}/investigate → 202 { analysisId, status: Queued, statusUrl }
///   GET  /analyses/{id}              → poll until Succeeded with a grounded result
///
/// The real AnalysisWorker BackgroundService runs in the test host; the AI service
/// client is a deterministic stub — Gemini is never called (brief §41).
/// </summary>
[Collection("database")]
public sealed class IncidentInvestigationApiTests
{
    // The API serializes enums as strings; the test-side reader needs the same converter.
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(
        System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly DatabaseFixture _fixture;

    public IncidentInvestigationApiTests(DatabaseFixture fixture) => _fixture = fixture;

    private TestApi NewApi(FakeAiClient? ai = null)
        => new(_fixture.CreateFactory(services => services.AddSingleton<IAiServiceClient>(ai ?? new FakeAiClient())));

    [Fact]
    public async Task Engineer_InvestigatesIncident_202_ThenPolledCompleted_WithGroundedResult()
    {
        var ai = new FakeAiClient();
        var api = NewApi(ai);
        var (token, _) = await api.RegisterAsync($"inc-eng-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(token, "Incident Project");
        var incidentId = await CreateIncidentAsync(api, token, projectId, "HTTP 401 after JWT signing-key rotation");

        using var client = api.NewClient(token);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incidentId}/investigate",
            new { requestId = $"req-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var accepted = await response.Content.ReadFromJsonAsync<InvestigationAcceptedResponse>();
        Assert.NotNull(accepted);
        Assert.Equal("Queued", accepted!.Status);
        Assert.Equal($"/api/v1/analyses/{accepted.AnalysisId}", accepted.StatusUrl);

        // The worker (hosted service in the test host) completes the job; poll.
        var status = await PollUntilTerminalAsync(api, token, accepted.AnalysisId);

        Assert.Equal("Succeeded", status.Status);
        Assert.Equal("IncidentInvestigation", status.Type);
        Assert.Equal(incidentId, status.IncidentId);
        Assert.Equal("incident-v1", status.ResultSchemaVersion);
        Assert.Equal("mock-gemini-3.1-flash-lite", status.Model);
        Assert.Equal("incident-v1", status.PromptVersion);
        Assert.Null(status.Error);

        Assert.NotNull(status.Result);
        using var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(status.Result));
        var candidates = doc.RootElement.GetProperty("rootCauseCandidates");
        Assert.True(candidates.GetArrayLength() >= 1);
        var candidate = candidates[0];
        Assert.True(candidate.GetProperty("confidence").GetDouble() is >= 0.0 and <= 1.0);
        Assert.True(candidate.GetProperty("evidenceIds").GetArrayLength() >= 1);
        Assert.True(candidate.GetProperty("evidenceIds")[0].GetString()!.StartsWith("chunk:", StringComparison.Ordinal));

        // The worker passed the normalized incident context to the AI service.
        Assert.NotNull(ai.Received);
        Assert.Equal("HTTP 401 after JWT signing-key rotation", ai.Received!.Incident.Title);
        Assert.Equal(projectId, ai.Received.ProjectId);
        Assert.Equal(accepted.AnalysisId, ai.Received.AnalysisId);

        // Audit: requested (submit) + started + completed (worker).
        using var auditClient = api.NewClient(token);
        var auditResponse = await auditClient.GetAsync($"/api/v1/audit-logs?projectId={projectId}&page=1&pageSize=50");
        auditResponse.EnsureSuccessStatusCode();
        var auditBody = await auditResponse.Content.ReadFromJsonAsync<AuditPage>();
        Assert.NotNull(auditBody);
        var actions = auditBody!.Items.Select(a => a.Action).ToList();
        Assert.Contains("AnalysisRequested", actions);
        Assert.Contains("AnalysisStarted", actions);
        Assert.Contains("AnalysisCompleted", actions);
    }

    [Fact]
    public async Task Engineer_GetsAnalysisTrace_AfterCompletedRun()
    {
        var ai = new FakeAiClient();
        var api = NewApi(ai);
        var (token, _) = await api.RegisterAsync($"inc-trace-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(token, "Trace Project");
        var incidentId = await CreateIncidentAsync(api, token, projectId, "Trace incident");

        using var client = api.NewClient(token);
        var submit = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incidentId}/investigate", new { });
        var accepted = await submit.Content.ReadFromJsonAsync<InvestigationAcceptedResponse>();
        await PollUntilTerminalAsync(api, token, accepted!.AnalysisId);

        var traceResponse = await client.GetAsync($"/api/v1/analyses/{accepted.AnalysisId}/trace");
        traceResponse.EnsureSuccessStatusCode();
        var trace = await traceResponse.Content.ReadFromJsonAsync<AnalysisTraceResponse>();

        Assert.NotNull(trace);
        Assert.Equal(accepted.AnalysisId, trace!.AnalysisId);
        Assert.Equal("IncidentInvestigation", trace.Type);
        Assert.Equal("Succeeded", trace.Status);
        Assert.Equal("trace-v1", trace.TraceSchemaVersion);

        var stageNames = trace.Stages.Select(s => s.Name).ToList();
        Assert.Contains("Context", stageNames);
        Assert.Contains("AI Analysis", stageNames);
        Assert.Contains("Persistence", stageNames);
        Assert.All(trace.Stages, s => Assert.True(s.DurationMs >= 0));

        Assert.NotNull(trace.Retrieval);
        Assert.Equal(1, trace.Retrieval!.SelectedCount);
        Assert.Equal("chunk:auth-001", trace.Retrieval.Items[0].Id);
        Assert.Equal(1, trace.Retrieval.Items[0].KeywordRank);
        Assert.Null(trace.FailureCode);
        Assert.Null(trace.FailureCategory);
    }

    [Fact]
    public async Task CrossProjectIsolation_UserCannotInvestigateOrViewOtherProject()
    {
        var ai = new FakeAiClient();
        var api = NewApi(ai);

        // Project A + incident A (owner A).
        var (tokenA, _) = await api.RegisterAsync($"inc-a-{Guid.NewGuid():N}@test.dev");
        var projectA = await api.CreateProjectAsync(tokenA, "Project A");
        var incidentA = await CreateIncidentAsync(api, tokenA, projectA, "A incident");

        // Project B + incident B (owner B).
        var (tokenB, _) = await api.RegisterAsync($"inc-b-{Guid.NewGuid():N}@test.dev");
        var projectB = await api.CreateProjectAsync(tokenB, "Project B");
        var incidentB = await CreateIncidentAsync(api, tokenB, projectB, "B incident");

        // B submits an investigation so an analysis for project B exists.
        using var clientB = api.NewClient(tokenB);
        var submitB = await clientB.PostAsJsonAsync(
            $"/api/v1/incidents/{incidentB}/investigate", new { requestId = $"req-b-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Accepted, submitB.StatusCode);
        var acceptedB = await submitB.Content.ReadFromJsonAsync<InvestigationAcceptedResponse>();
        await PollUntilTerminalAsync(api, tokenB, acceptedB!.AnalysisId);

        // A cannot investigate B's incident (404 — existence is not revealed).
        using var clientA = api.NewClient(tokenA);
        var crossInvestigate = await clientA.PostAsJsonAsync(
            $"/api/v1/incidents/{incidentB}/investigate", new { });
        Assert.Equal(HttpStatusCode.NotFound, crossInvestigate.StatusCode);

        // A cannot retrieve B's analysis (404).
        var crossStatus = await clientA.GetAsync($"/api/v1/analyses/{acceptedB.AnalysisId}");
        Assert.Equal(HttpStatusCode.NotFound, crossStatus.StatusCode);

        // A CAN investigate and view their own incident.
        var ownSubmit = await clientA.PostAsJsonAsync(
            $"/api/v1/incidents/{incidentA}/investigate", new { requestId = $"req-a-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Accepted, ownSubmit.StatusCode);
        var acceptedA = await ownSubmit.Content.ReadFromJsonAsync<InvestigationAcceptedResponse>();
        var ownStatus = await PollUntilTerminalAsync(api, tokenA, acceptedA!.AnalysisId);
        Assert.Equal("Succeeded", ownStatus.Status);

        // B cannot view A's analysis either.
        using var clientB2 = api.NewClient(tokenB);
        var crossStatusB = await clientB2.GetAsync($"/api/v1/analyses/{acceptedA.AnalysisId}");
        Assert.Equal(HttpStatusCode.NotFound, crossStatusB.StatusCode);

        // B cannot read A's analysis trace either (same authorization boundary).
        var crossTraceB = await clientB2.GetAsync($"/api/v1/analyses/{acceptedA.AnalysisId}/trace");
        Assert.Equal(HttpStatusCode.NotFound, crossTraceB.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotSubmitInvestigation_Gets403()
    {
        var api = NewApi();
        var (ownerToken, _) = await api.RegisterAsync($"inc-owner-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(ownerToken, "Viewer Project");
        var incidentId = await CreateIncidentAsync(api, ownerToken, projectId, "Viewer incident");

        using (var add = api.NewClient(ownerToken))
        {
            (await add.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
                new { email = "viewer@changelens.dev", role = ProjectRole.Viewer })).EnsureSuccessStatusCode();
        }

        var viewerToken = await api.LoginAsSeededAsync("viewer@changelens.dev");

        using var client = api.NewClient(viewerToken);
        var response = await client.PostAsJsonAsync($"/api/v1/incidents/{incidentId}/investigate", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonMember_Investigate_Gets404()
    {
        var api = NewApi();
        var (ownerToken, _) = await api.RegisterAsync($"inc-own2-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(ownerToken, "Isolated Project");
        var incidentId = await CreateIncidentAsync(api, ownerToken, projectId, "Isolated incident");
        var (outsiderToken, _) = await api.RegisterAsync($"inc-out-{Guid.NewGuid():N}@test.dev");

        using var client = api.NewClient(outsiderToken);
        var response = await client.PostAsJsonAsync($"/api/v1/incidents/{incidentId}/investigate", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async Task<Guid> CreateIncidentAsync(
        TestApi api, string token, Guid projectId, string title)
    {
        using var client = api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            projectId,
            title,
            severity = "Sev1",
            status = "Open",
            environment = "production",
            summary = "Auth requests started failing after signing-key rotation.",
            events = new[]
            {
                new
                {
                    occurredAtUtc = DateTime.UtcNow.AddMinutes(-30),
                    type = "Deployment",
                    source = "cicd",
                    message = "Deployed signing-key rotation"
                },
                new
                {
                    occurredAtUtc = DateTime.UtcNow.AddMinutes(-25),
                    type = "Error",
                    source = "api",
                    message = "JwtSecurityTokenHandler: IDX10503 signature validation failed"
                }
            }
        });
        response.EnsureSuccessStatusCode();

        var incident = await response.Content.ReadFromJsonAsync<IncidentResponse>(JsonOptions);
        return incident!.Id;
    }

    private static async Task<AnalysisStatusResponse> PollUntilTerminalAsync(
        TestApi api, string token, Guid analysisId, int timeoutSeconds = 20)
    {
        using var client = api.NewClient(token);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/v1/analyses/{analysisId}");
            response.EnsureSuccessStatusCode();

            var status = await response.Content.ReadFromJsonAsync<AnalysisStatusResponse>();
            Assert.NotNull(status);

            if (status!.Status is "Succeeded" or "Failed")
            {
                return status;
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"Analysis {analysisId} did not reach a terminal state within {timeoutSeconds}s.");
    }

    private sealed class FakeAiClient : IAiServiceClient
    {
        public IncidentAnalysisRequestDto? Received { get; private set; }

        public Task<IncidentAnalysisResponseDto> AnalyzeIncidentAsync(
            IncidentAnalysisRequestDto request, CancellationToken ct)
        {
            Received = request;

            return Task.FromResult(new IncidentAnalysisResponseDto
            {
                AnalysisType = "incident",
                Result = new IncidentAnalysisResultDto
                {
                    RootCauseCandidates =
                    [
                        new RootCauseCandidateDto
                        {
                            Id = "cand-1",
                            Title = "Signing-key rotation invalidated issued tokens",
                            Confidence = 0.7,
                            Status = "Candidate",
                            EvidenceIds = ["chunk:auth-001"],
                            Reasoning = "The timeline places the deployment before the first 401."
                        }
                    ],
                    Remediation = new RemediationDto
                    {
                        ImmediateMitigation = "Validate the new key against the token issuer.",
                        InvestigationSteps = ["Correlate the first 401 with the rotation window."],
                        RollbackConsideration = "Evaluate rolling the rotation back.",
                        InsufficientEvidence = false
                    },
                    Unknowns = ["No database telemetry was available."],
                    Evidence =
                    [
                        new EvidenceItemDto
                        {
                            Id = "chunk:auth-001",
                            Type = "Document",
                            Reference = "chunk:auth-001",
                            Summary = "auth-001-jwt-key-rotation incident chunk."
                        }
                    ]
                },
                Usage = new AnalysisUsageDto
                {
                    Model = "mock-gemini-3.1-flash-lite",
                    PromptVersion = "incident-v1",
                    ValidationStatus = "valid"
                },
                Trace = new RetrievalTraceDto
                {
                    Queries = ["HTTP 401 after JWT signing-key rotation"],
                    CandidateCount = 2,
                    SelectedCount = 1,
                    MaxChunks = 20,
                    MaxCharsPerChunk = 12000,
                    Items =
                    [
                        new RetrievalTraceItemDto
                        {
                            Id = "chunk:auth-001",
                            DocumentType = "Runbook",
                            Path = "auth-001-jwt-key-rotation.md",
                            KeywordRank = 1,
                            VectorScore = 0.88
                        }
                    ]
                }
            });
        }

        public Task<ChangeRiskAnalysisResponse> AnalyzeChangeRiskAsync(
            AnalyzeChangeRiskRequest request, CancellationToken ct)
            => throw new NotSupportedException("Not used in incident investigation tests.");
    }

    private sealed class AuditPage
    {
        public List<AuditLogItem> Items { get; init; } = [];
    }

    private sealed class AuditLogItem
    {
        public string Action { get; init; } = string.Empty;
    }
}
