using System.Net;
using System.Net.Http.Json;
using ChangeLens.Api.IntegrationTests.Infrastructure;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace ChangeLens.Api.IntegrationTests;

/// <summary>
/// End-to-end API tests for the Phase 2 vertical slice. The real AiServiceClient is
/// replaced with a deterministic stub — Gemini is never called in the default suite.
/// </summary>
[Collection("database")]
public sealed class AnalysesApiTests
{
    private readonly DatabaseFixture _fixture;

    public AnalysesApiTests(DatabaseFixture fixture) => _fixture = fixture;

    private TestApi NewApi(FakeAiClient? ai = null)
        => new(_fixture.CreateFactory(services => services.AddSingleton<IAiServiceClient>(ai ?? new FakeAiClient())));

    [Fact]
    public async Task RequiresAuthentication()
    {
        var api = NewApi();
        using var client = api.NewClient();
        var response = await client.PostAsJsonAsync("/api/v1/analyses/change-risk", ValidBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ViewerMember_Gets403()
    {
        var api = NewApi();
        var (ownerToken, _) = await api.RegisterAsync($"ana-owner-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(ownerToken, "Analysis Project");

        using (var add = api.NewClient(ownerToken))
        {
            (await add.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
                new { email = "viewer@changelens.dev", role = ProjectRole.Viewer })).EnsureSuccessStatusCode();
        }

        var viewerToken = await api.LoginAsSeededAsync("viewer@changelens.dev");

        using var client = api.NewClient(viewerToken);
        var response = await client.PostAsJsonAsync("/api/v1/analyses/change-risk", ValidBody(projectId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonMember_Gets404()
    {
        var api = NewApi();
        var (ownerToken, _) = await api.RegisterAsync($"ana-iso-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(ownerToken, "Isolated");
        var (outsiderToken, _) = await api.RegisterAsync($"ana-out-{Guid.NewGuid():N}@test.dev");

        using var client = api.NewClient(outsiderToken);
        var response = await client.PostAsJsonAsync("/api/v1/analyses/change-risk", ValidBody(projectId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvalidBody_Returns400()
    {
        var api = NewApi();
        var (token, _) = await api.RegisterAsync($"ana-inv-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(token);

        using var client = api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/analyses/change-risk",
            new { projectId, changeSummary = "summary" }); // no changedFiles

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Engineer_RunsAnalysis_ReceivesValidatedResult_AndAiClientSawRequest()
    {
        var ai = new FakeAiClient();
        var api = NewApi(ai);
        var (token, userId) = await api.RegisterAsync($"ana-eng-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(token, "Risk Project");

        using var add = api.NewClient(token);
        (await add.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
            new { email = "viewer@changelens.dev", role = ProjectRole.Viewer })).EnsureSuccessStatusCode();

        var engineerEmail = $"eng-{Guid.NewGuid():N}@test.dev";
        var (engineerToken, _) = await api.RegisterAsync(engineerEmail);
        using (var addEng = api.NewClient(token))
        {
            (await addEng.PostAsJsonAsync($"/api/v1/projects/{projectId}/members",
                new { email = engineerEmail, role = ProjectRole.Engineer })).EnsureSuccessStatusCode();
        }

        using var client = api.NewClient(engineerToken);
        var response = await client.PostAsJsonAsync("/api/v1/analyses/change-risk",
            new
            {
                projectId,
                changeSummary = "Changed token refresh logic in AuthClient.cs.",
                changedFiles = new[]
                {
                    new { path = "src/AuthClient.cs", changeType = "modified", language = "csharp" }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ChangeRiskAnalysisResponse>();
        Assert.NotNull(body);
        Assert.Equal("MEDIUM", body.Result.RiskLevel);
        Assert.Equal(0.72, body.Result.Confidence);
        Assert.Equal("valid", body.Usage.ValidationStatus);

        Assert.NotNull(ai.Received);
        Assert.Equal(projectId, ai.Received!.ProjectId);
        Assert.Single(ai.Received.ChangedFiles);
        Assert.Equal("src/AuthClient.cs", ai.Received.ChangedFiles[0].Path);

        // The analysis is audit-logged.
        using var auditClient = api.NewClient(token);
        var auditResponse = await auditClient.GetAsync($"/api/v1/audit-logs?projectId={projectId}&page=1&pageSize=50");
        auditResponse.EnsureSuccessStatusCode();
        var auditBody = await auditResponse.Content.ReadFromJsonAsync<AuditPage>();
        Assert.NotNull(auditBody);
        Assert.Contains(auditBody.Items, a => a.Action == "AnalysisRequested");
    }

    [Fact]
    public async Task DemoScenario_ChangeRiskAnalysis_DiscoversEvidenceFromRoslynAndGraph()
    {
        // Mock end-to-end (brief §46): the real engine resolves the demo repo's JWT
        // key-rotation change (git HEAD → working tree), runs Roslyn, builds the
        // dependency graph, and enriches the AI request — the fake AI client grounds
        // its response in that evidence. No Gemini call happens.
        var ai = new FakeAiClient();
        var api = NewApi(ai);
        var (token, _) = await api.RegisterAsync($"ana-demo-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(token, "Demo Risk Project");

        using var client = api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/analyses/change-risk",
            new
            {
                projectId,
                changeSummary = "JWT signing key rotation: issue and validate against the full key history.",
                changedFiles = new[]
                {
                    new { path = "src/AcmePay.Application/Auth/TokenService.cs", changeType = "modified", language = "csharp" }
                },
                baseRevision = "HEAD"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ChangeRiskAnalysisResponse>();
        Assert.NotNull(body);
        Assert.Equal("valid", body!.Usage.ValidationStatus);

        // The AI request was enriched by Roslyn + the dependency graph (Phase 4).
        Assert.NotNull(ai.Received);
        Assert.Contains(ai.Received!.ChangedSymbols, s => s.Name == "IssueServiceToken");
        Assert.Contains(ai.Received.ChangedSymbols, s => s.Name == "TryValidateServiceToken");
        Assert.Contains(ai.Received.ImpactedSymbols, s => s.Name == "Program");
        Assert.NotEmpty(ai.Received.DependencyEdges);
        Assert.NotEmpty(ai.Received.DependencyPaths);
        Assert.Contains(ai.Received.ChangedFiles[0].SymbolsChanged, s => s == "IssueServiceToken");
    }

    [Fact]
    public async Task AiValidationFailure_SurfacesAs422_WithDetails()
    {
        var ai = new FakeAiClient { ExceptionToThrow = new AiValidationFailedException(
            "AI output failed validation after bounded repair.",
            new { attempts = 3, errors = new[] { "risk_factors[0] references no evidence id" } }) };

        var api = NewApi(ai);
        var (token, _) = await api.RegisterAsync($"ana-fail-{Guid.NewGuid():N}@test.dev");
        var projectId = await api.CreateProjectAsync(token);

        using var client = api.NewClient(token);
        var response = await client.PostAsJsonAsync("/api/v1/analyses/change-risk", ValidBody(projectId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemEnvelope>();
        Assert.NotNull(problem);
        Assert.Equal("ai_validation_failed", problem.Code);
        Assert.NotNull(problem.Details);
    }

    private static object ValidBody(Guid projectId) => new
    {
        projectId,
        changeSummary = "Changed token refresh logic.",
        changedFiles = new[]
        {
            new { path = "src/AuthClient.cs", changeType = "modified", language = "csharp" }
        }
    };

    private sealed class FakeAiClient : IAiServiceClient
    {
        public AnalyzeChangeRiskRequest? Received { get; private set; }

        public Exception? ExceptionToThrow { get; set; }

        public Task<ChangeRiskAnalysisResponse> AnalyzeChangeRiskAsync(
            AnalyzeChangeRiskRequest request, CancellationToken ct)
        {
            Received = request;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(new ChangeRiskAnalysisResponse
            {
                Result = new ChangeRiskResultDto
                {
                    RiskLevel = "MEDIUM",
                    Confidence = 0.72,
                    RiskFactors =
                    [
                        new RiskFactorDto
                        {
                            Title = "AuthClient modified",
                            Description = "Token refresh logic changed.",
                            Severity = "MEDIUM",
                            Evidence =
                            [
                                new EvidenceReferenceDto { Type = "ChangedFile", Reference = "change:src/AuthClient.cs" }
                            ]
                        }
                    ],
                    Evidence =
                    [
                        new EvidenceItemDto
                        {
                            Id = "change:src/AuthClient.cs",
                            Type = "ChangedFile",
                            Reference = "src/AuthClient.cs",
                            Summary = "Token refresh logic changed."
                        }
                    ]
                },
                Usage = new AnalysisUsageDto
                {
                    Model = "mock-gemini-3.7-flash",
                    PromptVersion = "risk-v1",
                    ValidationStatus = "valid",
                    RepairAttempts = 0
                }
            });
        }
    }

    private sealed class AuditPage
    {
        public List<AuditLogItem> Items { get; init; } = [];
    }

    private sealed class AuditLogItem
    {
        public string Action { get; init; } = string.Empty;
    }

    private sealed class ProblemEnvelope
    {
        public string Code { get; init; } = string.Empty;
        public object? Details { get; init; }
    }
}
