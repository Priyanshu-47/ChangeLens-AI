using System.Text.Json;
using ChangeLens.Application.Configuration;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Services;
using ChangeLens.Application.Tools;
using ChangeLens.Application.Tracing;
using ChangeLens.Domain.Analysis;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Incidents;
using ChangeLens.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChangeLens.UnitTests.Tools;

/// <summary>
/// Phase 8 controlled tool loop (docs/agent-tools.md): registry allowlist, argument
/// validation, project isolation, bounded execution, max-call limit, tool timeout,
/// trace + audit, and the adversarial cases from brief §40. Zero Gemini — everything
/// is deterministic fakes.
/// </summary>
public sealed class ToolLoopTests : ServiceTestBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private FakeAiClient _ai = new();

    private ToolLoopOrchestrator Loop(
        ToolRegistry registry, AnalysisOptions? options = null, FakeAiClient? ai = null)
    {
        _ai = ai ?? new FakeAiClient();
        return new ToolLoopOrchestrator(
            registry, _ai, Audit, Options.Create(options ?? new AnalysisOptions { MaxToolCalls = 3 }),
            NullLogger<ToolLoopOrchestrator>.Instance);
    }

    private static ToolRegistry Registry(params ITool[] tools) => new(tools);

    private async Task<(Guid projectId, Guid incidentId)> CreateIncidentAsync()
    {
        var projectId = await CreateProjectAsync();
        var incident = await Incidents.CreateAsync(new CreateIncidentRequest
        {
            ProjectId = projectId,
            Title = "HTTP 401 after JWT signing-key rotation",
            Severity = IncidentSeverity.Sev1,
            Status = IncidentStatus.Open,
            Events =
            [
                new CreateIncidentEventRequest
                {
                    Type = IncidentEventType.Deployment,
                    Message = "Deployed signing-key rotation",
                    Source = "cicd"
                },
                new CreateIncidentEventRequest
                {
                    Type = IncidentEventType.Error,
                    Message = "JwtSecurityTokenHandler: IDX10503 signature validation failed",
                    Source = "api"
                }
            ]
        }, CancellationToken.None);
        return (projectId, incident.Id);
    }

    private static IncidentAnalysisRequestDto Request(Guid projectId, Guid incidentId) => new()
    {
        AnalysisId = Guid.NewGuid(),
        ProjectId = projectId,
        Incident = new IncidentContextDto
        {
            Title = "HTTP 401 after JWT signing-key rotation",
            Severity = "Sev1",
            Status = "Open",
            Service = "acmepay-api"
        }
    };

    // ── registry ──────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_ResolvesRegisteredToolsAndDescribesCatalog()
    {
        var registry = Registry(new GetIncidentTool(Context), new GetRunbookTool(_ai));
        Assert.NotNull(registry.TryGet("get_incident"));
        Assert.NotNull(registry.TryGet("get_runbook"));
        Assert.Null(registry.TryGet("execute_sql")); // not in the allowlist

        var catalog = registry.Describe();
        Assert.Contains(catalog, t => t.Name == "get_incident");
        Assert.Contains(catalog, t => t.Name == "get_runbook");
    }

    // ── get_incident: validation + project isolation ───────────────────────────

    [Fact]
    public async Task GetIncident_ReturnsProjectScopedIncident()
    {
        var (projectId, incidentId) = await CreateIncidentAsync();
        var tool = new GetIncidentTool(Context);
        var ctx = new ToolExecutionContext(Guid.NewGuid(), projectId, incidentId, CancellationToken.None);

        var result = await tool.ExecuteAsync(ctx, Args("{ \"incidentId\": \"" + incidentId + "\" }"), CancellationToken.None);

        Assert.Equal(ToolStatus.Executed, result.Status);
        Assert.Contains($"incident:{incidentId:N}", result.EvidenceIds);
        using var doc = JsonDocument.Parse(result.OutputJson!);
        Assert.Equal("HTTP 401 after JWT signing-key rotation",
            doc.RootElement.GetProperty("payload").GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetIncident_CrossProject_ResolvesNotFound_NoExistenceLeak()
    {
        var (_, incidentId) = await CreateIncidentAsync();
        var otherProject = await CreateProjectAsync("Other Project");
        var tool = new GetIncidentTool(Context);
        var ctx = new ToolExecutionContext(Guid.NewGuid(), otherProject, incidentId, CancellationToken.None);

        var result = await tool.ExecuteAsync(ctx, Args($"{{ \"incidentId\": \"{incidentId}\" }}"), CancellationToken.None);

        Assert.Equal(ToolStatus.Rejected, result.Status);
        Assert.Equal(ToolErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task GetIncident_InvalidUuid_RejectedBeforeExecution()
    {
        var tool = new GetIncidentTool(Context);
        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args("{ \"incidentId\": \"not-a-uuid\" }"), CancellationToken.None);

        Assert.Equal(ToolStatus.Rejected, result.Status);
        Assert.Equal(ToolErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task GetIncident_MissingArgument_Rejected()
    {
        var tool = new GetIncidentTool(Context);
        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args("{}"), CancellationToken.None);

        Assert.Equal(ToolStatus.Rejected, result.Status);
        Assert.Equal(ToolErrorCode.InvalidArgument, result.ErrorCode);
    }

    // ── get_incident_timeline ──────────────────────────────────────────────────

    [Fact]
    public async Task GetIncidentTimeline_ReturnsChronologicalEventsWithEvidenceIds()
    {
        var (projectId, incidentId) = await CreateIncidentAsync();
        var tool = new GetIncidentTimelineTool(Context);
        var ctx = new ToolExecutionContext(Guid.NewGuid(), projectId, incidentId, CancellationToken.None);

        var result = await tool.ExecuteAsync(ctx, Args($"{{ \"incidentId\": \"{incidentId}\" }}"), CancellationToken.None);

        Assert.Equal(ToolStatus.Executed, result.Status);
        Assert.Equal(2, result.EvidenceIds.Count);
        Assert.All(result.EvidenceIds, id => Assert.StartsWith("incident-event:", id));
        using var doc = JsonDocument.Parse(result.OutputJson!);
        var events = doc.RootElement.GetProperty("payload").GetProperty("events");
        Assert.Equal(2, events.GetArrayLength());
        // Chronological: deployment before error.
        Assert.Equal("Deployment", events[0].GetProperty("type").GetString());
        Assert.Equal("Error", events[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetIncidentTimeline_CrossProject_NotFound()
    {
        var (_, incidentId) = await CreateIncidentAsync();
        var otherProject = await CreateProjectAsync("Other");
        var tool = new GetIncidentTimelineTool(Context);

        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), otherProject, incidentId, CancellationToken.None),
            Args($"{{ \"incidentId\": \"{incidentId}\" }}"), CancellationToken.None);

        Assert.Equal(ToolErrorCode.NotFound, result.ErrorCode);
    }

    // ── get_service ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetService_ReturnsProjectScopedService()
    {
        var projectId = await CreateProjectAsync();
        var service = await Services.CreateAsync(projectId, new CreateServiceRequest
        {
            Name = "acmepay-api",
            Language = "csharp"
        }, CancellationToken.None);
        var tool = new GetServiceTool(Context);

        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), projectId, Guid.NewGuid(), CancellationToken.None),
            Args($"{{ \"serviceId\": \"{service.Id}\" }}"), CancellationToken.None);

        Assert.Equal(ToolStatus.Executed, result.Status);
        Assert.Equal($"service:{service.Id:N}", Assert.Single(result.EvidenceIds));
    }

    [Fact]
    public async Task GetService_CrossProject_NotFound()
    {
        var projectId = await CreateProjectAsync();
        var service = await Services.CreateAsync(projectId, new CreateServiceRequest
        {
            Name = "acmepay-api"
        }, CancellationToken.None);
        var otherProject = await CreateProjectAsync("Other");
        var tool = new GetServiceTool(Context);

        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), otherProject, Guid.NewGuid(), CancellationToken.None),
            Args($"{{ \"serviceId\": \"{service.Id}\" }}"), CancellationToken.None);

        Assert.Equal(ToolErrorCode.NotFound, result.ErrorCode);
    }

    // ── get_dependency_paths: identifier safety + bounded traversal ─────────────

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://example.com")]
    public async Task GetDependencyPaths_RejectsPathLikeSymbols(string symbol)
    {
        var tool = new GetDependencyPathsTool(new FakeEngine());
        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args(JsonSerializer.Serialize(new { symbol }, Web)), CancellationToken.None);

        Assert.Equal(ToolStatus.Rejected, result.Status);
        Assert.Equal(ToolErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task GetDependencyPaths_ReturnsEvidenceIdsForResolvedEdges()
    {
        var engine = new FakeEngine
        {
            Paths =
            [
                new SymbolDependencyPathDto
                {
                    From = "AcmePay.Auth.TokenService",
                    To = "AcmePay.Program",
                    EdgeType = "REFERENCES_TYPE"
                }
            ],
            ResolvedSymbol = "AcmePay.Auth.TokenService"
        };
        var tool = new GetDependencyPathsTool(engine);

        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args("{ \"symbol\": \"TokenService\", \"maxDepth\": 2 }"), CancellationToken.None);

        Assert.Equal(ToolStatus.Executed, result.Status);
        Assert.Equal("dependency:AcmePay.Auth.TokenService -> AcmePay.Program",
            Assert.Single(result.EvidenceIds));
        Assert.Equal(2, engine.LastMaxDepth);
    }

    [Fact]
    public async Task GetDependencyPaths_RejectsOutOfRangeMaxDepth()
    {
        var engine = new FakeEngine();
        var tool = new GetDependencyPathsTool(engine);

        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args("{ \"symbol\": \"TokenService\", \"maxDepth\": 99 }"), CancellationToken.None);

        // Out-of-range arguments are rejected (validated, not silently clamped — brief §8),
        // so the engine is never reached. The engine additionally clamps defensively.
        Assert.Equal(ToolErrorCode.InvalidArgument, result.ErrorCode);
        Assert.Equal(0, engine.LastMaxDepth);
    }

    // ── retrieval tools (get_runbook / get_source_symbol / search_evidence) ─────

    [Fact]
    public async Task GetRunbook_ReturnsChunkEvidenceIds()
    {
        _ai.RetrievalResults = new RetrievalSearchResponseDto
        {
            Results =
            [
                new RetrievalSearchResultDto
                {
                    ChunkId = "abc-123", DocumentType = "Runbook",
                    Metadata = new Dictionary<string, object?> { ["title"] = "authentication-failure" },
                    Content = "Rotate keys."
                }
            ]
        };
        var tool = new GetRunbookTool(_ai);

        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args("{ \"query\": \"authentication failure\", \"topK\": 3 }"), CancellationToken.None);

        Assert.Equal(ToolStatus.Executed, result.Status);
        Assert.Equal("chunk:abc-123", Assert.Single(result.EvidenceIds));
        Assert.Equal("Runbook", _ai.LastRetrievalRequest!.DocumentTypes!.Single());
        Assert.Equal(3, _ai.LastRetrievalRequest.K);
    }

    [Fact]
    public async Task GetRunbook_EmptyQuery_Rejected()
    {
        var tool = new GetRunbookTool(_ai);
        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args("{ \"query\": \"   \" }"), CancellationToken.None);

        Assert.Equal(ToolErrorCode.InvalidArgument, result.ErrorCode);
        Assert.Null(_ai.LastRetrievalRequest); // rejected before execution
    }

    [Fact]
    public async Task SearchEvidence_UnknownDocumentType_Rejected()
    {
        var tool = new SearchEvidenceTool(_ai);
        var result = await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None),
            Args("{ \"query\": \"x\", \"documentType\": \"DROP_TABLE\" }"), CancellationToken.None);

        Assert.Equal(ToolErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task SearchEvidence_ForcesProjectScope()
    {
        var projectId = Guid.NewGuid();
        var tool = new SearchEvidenceTool(_ai);
        await tool.ExecuteAsync(
            new ToolExecutionContext(Guid.NewGuid(), projectId, Guid.NewGuid(), CancellationToken.None),
            Args("{ \"query\": \"JWT\" }"), CancellationToken.None);

        // The AI cannot override project scope: the context's project id is used.
        Assert.Equal(projectId, _ai.LastRetrievalRequest!.ProjectId);
    }

    // ── tool loop ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Loop_AiProposesTool_ApplicationExecutes_ThenFinal()
    {
        var (projectId, incidentId) = await CreateIncidentAsync();
        var toolLoop = Loop(Registry(
            new GetIncidentTool(Context),
            new GetDependencyPathsTool(new FakeEngine
            {
                ResolvedSymbol = "AcmePay.Auth.TokenService",
                Paths = [new SymbolDependencyPathDto { From = "AcmePay.Auth.TokenService", To = "AcmePay.Program", EdgeType = "REFERENCES_TYPE" }]
            })));
        _ai.Then(_ => Task.FromResult(ToolCallResponse("get_dependency_paths", new { symbol = "TokenService", maxDepth = 2 })));
        _ai.Then(Turn => Task.FromResult(ToolCallResponse("get_incident", new { incidentId })));
        _ai.Then(_ => Task.FromResult(FinalResponse()));

        var trace = new AnalysisTraceBuilder();
        var response = await toolLoop.ExecuteAsync(
            Request(projectId, incidentId),
            new ToolExecutionContext(Guid.NewGuid(), projectId, incidentId, CancellationToken.None),
            trace, CancellationToken.None);

        Assert.Equal("final", response.Kind);
        Assert.Equal("cand-1", response.Result!.RootCauseCandidates[0].Id);

        // Tool results were fed back into subsequent turns (propose → execute → feed
        // back → propose → execute → feed back → final).
        Assert.Equal(3, _ai.Requests.Count);
        Assert.Empty(_ai.Requests[0].ToolResults);
        Assert.Single(_ai.Requests[1].ToolResults);
        Assert.Equal("executed", _ai.Requests[1].ToolResults[0].Status);
        Assert.Equal(1, _ai.Requests[1].ToolResults[0].EvidenceIdsAccessor()); // dependency id
        Assert.Equal(2, _ai.Requests[2].ToolResults.Count);

        // Trace records both tool calls with real statuses.
        Assert.Equal(2, trace.ToolCalls.Count);
        Assert.All(trace.ToolCalls, c => Assert.Equal("Executed", c.Status));

        // Audit events written for both executions.
        var actions = Context.Set<AuditLog>()
            .Where(a => a.Action == AuditActions.ToolExecuted)
            .Select(a => a.DetailsJson)
            .ToList();
        Assert.Equal(2, actions.Count);
        Assert.Contains("get_dependency_paths", actions[0]);
        Assert.Contains("get_incident", actions[1]);
    }

    [Fact]
    public async Task Loop_UnknownTool_RejectedWithNotAllowed_AndLoopContinues()
    {
        var (projectId, incidentId) = await CreateIncidentAsync();
        var toolLoop = Loop(Registry(new GetIncidentTool(Context)));
        _ai.Then(_ => Task.FromResult(ToolCallResponse("execute_sql", new { query = "DROP TABLE" })));
        _ai.Then(_ => Task.FromResult(FinalResponse()));

        var trace = new AnalysisTraceBuilder();
        var response = await toolLoop.ExecuteAsync(
            Request(projectId, incidentId),
            new ToolExecutionContext(Guid.NewGuid(), projectId, incidentId, CancellationToken.None),
            trace, CancellationToken.None);

        Assert.Equal("final", response.Kind);
        Assert.Equal(1, _ai.Requests[1].ToolResults.Count);
        Assert.Equal("not_allowed", _ai.Requests[1].ToolResults[0].Status);
        Assert.Equal(ToolErrorCode.NotAllowed, _ai.Requests[1].ToolResults[0].ErrorCode);

        var audit = Context.Set<AuditLog>().Single(a => a.Action == AuditActions.ToolRejected);
        Assert.Contains("execute_sql", audit.DetailsJson!);
    }

    [Fact]
    public async Task Loop_InvalidArguments_Rejected_AndFedBack()
    {
        var (projectId, incidentId) = await CreateIncidentAsync();
        var toolLoop = Loop(Registry(new GetDependencyPathsTool(new FakeEngine())));
        _ai.Then(_ => Task.FromResult(ToolCallResponse("get_dependency_paths", new { symbol = "../etc/passwd" })));
        _ai.Then(_ => Task.FromResult(FinalResponse()));

        var trace = new AnalysisTraceBuilder();
        await toolLoop.ExecuteAsync(
            Request(projectId, incidentId),
            new ToolExecutionContext(Guid.NewGuid(), projectId, incidentId, CancellationToken.None),
            trace, CancellationToken.None);

        Assert.Equal("rejected", _ai.Requests[1].ToolResults[0].Status);
        Assert.Equal(ToolErrorCode.InvalidArgument, _ai.Requests[1].ToolResults[0].ErrorCode);
        Assert.Equal("Rejected", trace.ToolCalls[0].Status);
    }

    [Fact]
    public async Task Loop_MaxToolCallsExceeded_FailsWithSafetyLimit()
    {
        var (projectId, incidentId) = await CreateIncidentAsync();
        var toolLoop = Loop(Registry(new GetIncidentTool(Context)), new AnalysisOptions { MaxToolCalls = 2 });
        // Propose tools on every turn — never final.
        for (var i = 0; i < 4; i++)
        {
            _ai.Then(_ => Task.FromResult(ToolCallResponse("get_incident", new { incidentId = Guid.NewGuid() })));
        }

        var trace = new AnalysisTraceBuilder();
        await Assert.ThrowsAsync<ToolCallLimitExceededException>(() =>
            toolLoop.ExecuteAsync(
                Request(projectId, incidentId),
                new ToolExecutionContext(Guid.NewGuid(), projectId, incidentId, CancellationToken.None),
                trace, CancellationToken.None));
    }

    [Fact]
    public async Task Loop_ToolTimeout_ReturnsSafeTimeoutResult()
    {
        var (projectId, incidentId) = await CreateIncidentAsync();
        var slow = new SlowTool();
        var toolLoop = Loop(Registry(slow), new AnalysisOptions { MaxToolCalls = 3, ToolTimeoutSeconds = 1 });
        _ai.Then(_ => Task.FromResult(ToolCallResponse("slow_tool", new { })));
        _ai.Then(_ => Task.FromResult(FinalResponse()));

        var trace = new AnalysisTraceBuilder();
        await toolLoop.ExecuteAsync(
            Request(projectId, incidentId),
            new ToolExecutionContext(Guid.NewGuid(), projectId, incidentId, CancellationToken.None),
            trace, CancellationToken.None);

        Assert.Equal("timeout", _ai.Requests[1].ToolResults[0].Status);
        Assert.Equal(ToolErrorCode.Timeout, _ai.Requests[1].ToolResults[0].ErrorCode);
        Assert.Equal("Failed", trace.ToolCalls[0].Status);
    }

    [Fact]
    public async Task Loop_CrossProjectIncident_ResolvesNotFound_InTheLoop()
    {
        var (_, incidentId) = await CreateIncidentAsync();
        var otherProject = await CreateProjectAsync("Other");
        var toolLoop = Loop(Registry(new GetIncidentTool(Context)));
        _ai.Then(_ => Task.FromResult(ToolCallResponse("get_incident", new { incidentId })));
        _ai.Then(_ => Task.FromResult(FinalResponse()));

        var trace = new AnalysisTraceBuilder();
        var response = await toolLoop.ExecuteAsync(
            Request(otherProject, Guid.NewGuid()),
            new ToolExecutionContext(Guid.NewGuid(), otherProject, Guid.NewGuid(), CancellationToken.None),
            trace, CancellationToken.None);

        Assert.Equal("final", response.Kind);
        Assert.Equal("rejected", _ai.Requests[1].ToolResults[0].Status);
        Assert.Equal(ToolErrorCode.NotFound, _ai.Requests[1].ToolResults[0].ErrorCode);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static JsonElement Args(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static IncidentAnalysisResponseDto ToolCallResponse(string name, object args) => new()
    {
        AnalysisType = "incident",
        Kind = "tool_call",
        ToolCall = new ToolCallDto
        {
            Id = "tool-" + name,
            Name = name,
            Arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(args, Web), Web)!
        },
        Usage = new AnalysisUsageDto { Model = "mock", ValidationStatus = "valid" }
    };

    private static IncidentAnalysisResponseDto FinalResponse() => new()
    {
        AnalysisType = "incident",
        Kind = "final",
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
                    EvidenceIds = ["chunk:runbook-1"],
                    Reasoning = "Tool-surfaced evidence supports the hypothesis."
                }
            ],
            Remediation = new RemediationDto { InsufficientEvidence = false },
            Unknowns = [],
            Evidence = [new EvidenceItemDto { Id = "chunk:runbook-1", Reference = "chunk:runbook-1" }]
        },
        Usage = new AnalysisUsageDto { Model = "mock", ValidationStatus = "valid", PromptVersion = "incident-tools-v1" }
    };

    private sealed class FakeEngine : IChangeAnalysisEngine
    {
        public string? ResolvedSymbol { get; set; }
        public List<SymbolDependencyPathDto> Paths { get; set; } = [];
        public int LastMaxDepth { get; private set; }

        public ChangeModelDto BuildChangeModel(AnalyzeChangeRiskRequest request)
            => throw new NotSupportedException();

        public SymbolDependencyPathsDto FindDependencyPaths(string symbol, int maxDepth)
        {
            LastMaxDepth = maxDepth;
            return new SymbolDependencyPathsDto { ResolvedSymbol = ResolvedSymbol, Paths = Paths, Warnings = [] };
        }
    }

    private sealed class FakeAiClient : IAiServiceClient
    {
        private readonly Queue<Func<IncidentAnalysisRequestDto, Task<IncidentAnalysisResponseDto>>> _behaviors = new();

        public List<IncidentAnalysisRequestDto> Requests { get; } = [];
        public RetrievalSearchRequestDto? LastRetrievalRequest { get; private set; }
        public RetrievalSearchResponseDto RetrievalResults { get; set; } = new();

        public void Then(Func<IncidentAnalysisRequestDto, Task<IncidentAnalysisResponseDto>> behavior)
            => _behaviors.Enqueue(behavior);

        public Task<IncidentAnalysisResponseDto> AnalyzeIncidentAsync(
            IncidentAnalysisRequestDto request, CancellationToken ct)
        {
            // The HTTP boundary serializes a snapshot per call; the loop mutates the same
            // request instance between turns, so capture the per-turn tool results here.
            Requests.Add(new IncidentAnalysisRequestDto
            {
                AnalysisId = request.AnalysisId,
                ProjectId = request.ProjectId,
                Incident = request.Incident,
                PromptVersion = request.PromptVersion,
                MaxEvidenceChunks = request.MaxEvidenceChunks,
                MaxCharsPerChunk = request.MaxCharsPerChunk,
                ToolCatalog = request.ToolCatalog.ToList(),
                ToolResults = request.ToolResults.ToList()
            });
            return _behaviors.Count > 0
                ? _behaviors.Dequeue()(request)
                : Task.FromResult(FinalResponse());
        }

        public Task<ChangeRiskAnalysisResponse> AnalyzeChangeRiskAsync(
            AnalyzeChangeRiskRequest request, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RetrievalSearchResponseDto> RetrievalSearchAsync(
            RetrievalSearchRequestDto request, CancellationToken ct)
        {
            LastRetrievalRequest = request;
            return Task.FromResult(RetrievalResults);
        }
    }

    private sealed class SlowTool : ITool
    {
        public string Name => "slow_tool";
        public string Description => "Hangs until the per-tool timeout fires.";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
        public Dictionary<string, object?> InputSchema => new() { ["type"] = "object" };

        public async Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context, JsonElement arguments, CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }
}

internal static class ToolResultExtensions
{
    /// <summary>Counts evidence ids declared in the serialized tool output.</summary>
    public static int EvidenceIdsAccessor(this ToolResultItemDto item)
    {
        using var doc = JsonDocument.Parse(item.Output!);
        return doc.RootElement.GetProperty("evidenceIds").GetArrayLength();
    }
}
