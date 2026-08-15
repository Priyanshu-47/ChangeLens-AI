using System.Text.Json;
using ChangeLens.Application.Configuration;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Services;
using ChangeLens.Application.Tools;
using ChangeLens.Domain.Analysis;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Incidents;
using ChangeLens.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChangeLens.UnitTests.Services;

/// <summary>
/// Worker-side orchestration (brief §24–27): state transitions, transient retries,
/// per-job timeout, failure persistence, audit events.
/// </summary>
public sealed class IncidentInvestigationOrchestratorTests : ServiceTestBase
{
    private readonly FakeAiClient _ai = new();

    private IncidentInvestigationOrchestrator Orchestrator(AnalysisOptions? options = null)
    {
        var opts = options ?? new AnalysisOptions { MaxRetries = 1, RetryBackoffSeconds = 0, JobTimeoutSeconds = 30 };
        var toolLoop = new ToolLoopOrchestrator(
            new ToolRegistry([]), _ai, Audit, Options.Create(opts),
            NullLogger<ToolLoopOrchestrator>.Instance);
        return new IncidentInvestigationOrchestrator(
            Context, _ai, toolLoop, Audit, Options.Create(opts),
            NullLogger<IncidentInvestigationOrchestrator>.Instance);
    }

    private async Task<(Guid projectId, Guid incidentId, Guid runId, AnalysisRun run)> CreateIncidentWithRunAsync()
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
                    Type = IncidentEventType.Error,
                    Message = "JwtSecurityTokenHandler: IDX10503 signature validation failed",
                    Source = "api"
                }
            ]
        }, CancellationToken.None);

        var run = new AnalysisRun
        {
            ProjectId = projectId,
            Type = "IncidentInvestigation",
            Status = AnalysisStatus.Queued,
            IncidentId = incident.Id,
            QueuedAtUtc = DateTime.UtcNow
        };
        Context.Set<AnalysisRun>().Add(run);
        await Context.SaveChangesAsync(CancellationToken.None);
        return (projectId, incident.Id, run.Id, run);
    }

    [Fact]
    public async Task Success_TransitionsQueuedToSucceeded_AndPersistsResult()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == runId);
        Assert.Equal(AnalysisStatus.Succeeded, run.Status);
        Assert.Equal("mock", run.Model);
        Assert.Equal("incident-v1", run.PromptVersion);
        Assert.Equal("incident-v1", run.ResultSchemaVersion);
        Assert.NotNull(run.ResultJson);
        Assert.NotNull(run.StartedAtUtc);
        Assert.NotNull(run.CompletedAtUtc);

        using var doc = JsonDocument.Parse(run.ResultJson!);
        Assert.Equal("cand-1", doc.RootElement.GetProperty("rootCauseCandidates")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Success_AuditsStartedAndCompleted()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var actions = Context.Set<AuditLog>()
            .Where(a => a.ResourceId == runId.ToString())
            .Select(a => a.Action)
            .ToList();
        Assert.Contains(AuditActions.AnalysisStarted, actions);
        Assert.Contains(AuditActions.AnalysisCompleted, actions);
    }

    [Fact]
    public async Task ValidationFailure_MarksFailed_AiValidationFailed_NoRetry()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();
        _ai.Then(_ => throw new AiValidationFailedException("AI output failed validation after bounded repair."));

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == runId);
        Assert.Equal(AnalysisStatus.Failed, run.Status);
        Assert.Equal(AnalysisFailureCode.AiValidationFailed, run.FailureCode);
        Assert.Contains("AI output failed validation", run.Error);
        Assert.Equal(1, _ai.IncidentCalls); // never retried
    }

    [Fact]
    public async Task TransientRateLimit_IsRetried_ThenSucceeds()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();
        _ai.Then(_ => throw new AiRateLimitedException("quota"));
        _ai.Then(_ => Task.FromResult(Success()));

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == runId);
        Assert.Equal(AnalysisStatus.Succeeded, run.Status);
        Assert.Equal(2, _ai.IncidentCalls); // initial + one retry
    }

    [Fact]
    public async Task PersistentRateLimit_MarksFailed_LlmRateLimited()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();
        _ai.Then(_ => throw new AiRateLimitedException("quota"));
        _ai.Then(_ => throw new AiRateLimitedException("quota"));

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == runId);
        Assert.Equal(AnalysisStatus.Failed, run.Status);
        Assert.Equal(AnalysisFailureCode.LlmRateLimited, run.FailureCode);
        Assert.Equal(2, _ai.IncidentCalls); // bounded: initial + max retries
    }

    [Fact]
    public async Task Timeout_MarksFailed_JobTimeout()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();
        // AI call hangs until the per-job timeout (linked CTS) cancels it.
        _ai.Then(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Success();
        });

        await Orchestrator(new AnalysisOptions { MaxRetries = 0, JobTimeoutSeconds = 1 })
            .RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == runId);
        Assert.Equal(AnalysisStatus.Failed, run.Status);
        Assert.Equal(AnalysisFailureCode.JobTimeout, run.FailureCode);
    }

    [Fact]
    public async Task AlreadySucceededRun_IsSkipped_NoAiCall()
    {
        var (_, incidentId, runId, run) = await CreateIncidentWithRunAsync();
        run.Status = AnalysisStatus.Succeeded;

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        Assert.Equal(0, _ai.IncidentCalls); // stale/double-enqueued job does nothing
        Assert.Equal(AnalysisStatus.Succeeded, run.Status);
    }

    [Fact]
    public async Task IncidentContext_FlowsToAiRequest()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        Assert.NotNull(_ai.Received);
        Assert.Equal("HTTP 401 after JWT signing-key rotation", _ai.Received!.Incident.Title);
        Assert.Contains(_ai.Received.Incident.Symptoms, s => s.Contains("IDX10503"));
    }

    [Fact]
    public async Task Success_PersistsTrace_WithStagesAndRetrieval()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();

        await Orchestrator().RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == runId);
        Assert.Equal("trace-v1", run.TraceSchemaVersion);
        Assert.NotNull(run.TraceJson);

        using var doc = JsonDocument.Parse(run.TraceJson!);
        var root = doc.RootElement;
        var stages = root.GetProperty("stages").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("Context", stages);
        Assert.Contains("AI Analysis", stages);
        Assert.Contains("Persistence", stages);
        // All stages carry real durations.
        Assert.All(
            root.GetProperty("stages").EnumerateArray(),
            s => Assert.True(s.GetProperty("durationMs").GetInt64() >= 0));
        // Retrieval trace from the AI service is attached verbatim.
        var retrieval = root.GetProperty("retrieval");
        Assert.Equal(2, retrieval.GetProperty("selectedCount").GetInt32());
        Assert.Equal("chunk:abc", retrieval.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failure").ValueKind);
    }

    [Fact]
    public async Task Failure_PersistsTrace_WithFailureCategory()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();
        _ai.Then(_ => throw new AiUnavailableException("down"));

        await Orchestrator(new AnalysisOptions { MaxRetries = 0 })
            .RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == runId);
        Assert.Equal(AnalysisStatus.Failed, run.Status);
        Assert.NotNull(run.TraceJson);

        using var doc = JsonDocument.Parse(run.TraceJson!);
        var root = doc.RootElement;
        Assert.Equal(AnalysisFailureCode.AiUnavailable, root.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal(AnalysisFailureCategory.AiProvider, root.GetProperty("failure").GetProperty("category").GetString());
        // The AI stage is marked Failed with the category recorded.
        var aiStage = root.GetProperty("stages").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == "AI Analysis");
        Assert.Equal("Failed", aiStage.GetProperty("status").GetString());
        Assert.Equal(AnalysisFailureCategory.AiProvider, aiStage.GetProperty("metadata").GetProperty("failureCategory").GetString());
    }

    [Fact]
    public async Task Failure_IsAudited_AsAnalysisFailed()
    {
        var (_, incidentId, runId, _) = await CreateIncidentWithRunAsync();
        _ai.Then(_ => throw new AiUnavailableException("down"));

        // MaxRetries=0: a single AiUnavailable is a terminal failure, not a retry.
        await Orchestrator(new AnalysisOptions { MaxRetries = 0 })
            .RunAsync(new AnalysisJob(runId, Guid.Empty, incidentId, null), CancellationToken.None);

        var actions = Context.Set<AuditLog>()
            .Where(a => a.ResourceId == runId.ToString())
            .Select(a => a.Action)
            .ToList();
        Assert.Contains(AuditActions.AnalysisStarted, actions);
        Assert.Contains(AuditActions.AnalysisFailed, actions);
        Assert.DoesNotContain(AuditActions.AnalysisCompleted, actions);
    }

    private static IncidentAnalysisResponseDto Success() => new()
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
                    EvidenceIds = ["chunk:abc"]
                }
            ],
            Remediation = new RemediationDto { InsufficientEvidence = false }
        },
        Usage = new AnalysisUsageDto { Model = "mock", PromptVersion = "incident-v1", ValidationStatus = "valid" },
        Trace = new RetrievalTraceDto
        {
            Queries = ["HTTP 401 after JWT signing-key rotation"],
            CandidateCount = 3,
            SelectedCount = 2,
            MaxChunks = 20,
            MaxCharsPerChunk = 12000,
            Items =
            [
                new RetrievalTraceItemDto
                {
                    Id = "chunk:abc",
                    DocumentType = "Runbook",
                    Path = "auth-001-jwt-key-rotation.md",
                    KeywordRank = 1,
                    VectorScore = 0.9
                },
                new RetrievalTraceItemDto
                {
                    Id = "chunk:def",
                    DocumentType = "Incident",
                    Path = "authentication-failure.md",
                    KeywordRank = 2
                }
            ]
        }
    };

    private sealed class FakeAiClient : IAiServiceClient
    {
        private readonly Queue<Func<CancellationToken, Task<IncidentAnalysisResponseDto>>> _behaviors = new();

        public int IncidentCalls { get; private set; }

        public IncidentAnalysisRequestDto? Received { get; private set; }

        public void Then(Func<CancellationToken, Task<IncidentAnalysisResponseDto>> behavior)
            => _behaviors.Enqueue(behavior);

        public void Then(Func<Task<IncidentAnalysisResponseDto>> behavior)
            => _behaviors.Enqueue(_ => behavior());

        public Task<IncidentAnalysisResponseDto> AnalyzeIncidentAsync(
            IncidentAnalysisRequestDto request, CancellationToken ct)
        {
            IncidentCalls++;
            Received = request;
            return _behaviors.Count > 0 ? _behaviors.Dequeue()(ct) : Task.FromResult(Success());
        }

        public Task<ChangeRiskAnalysisResponse> AnalyzeChangeRiskAsync(
            AnalyzeChangeRiskRequest request, CancellationToken ct)
            => throw new NotSupportedException("Not used in incident orchestrator tests.");

        public Task<RetrievalSearchResponseDto> RetrievalSearchAsync(
            RetrievalSearchRequestDto request, CancellationToken ct)
            => throw new NotSupportedException("Not used in incident orchestrator tests.");
    }
}
