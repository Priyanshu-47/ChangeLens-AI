using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Services;
using ChangeLens.Domain.Analysis;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Incidents;
using ChangeLens.Domain.Projects;
using ChangeLens.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChangeLens.UnitTests.Services;

/// <summary>POST /incidents/{id}/investigate (202) + GET /analyses/{id} (brief §5–8, §34).</summary>
public sealed class IncidentInvestigationServiceTests : ServiceTestBase
{
    private readonly FakeJobQueue _queue = new();

    private IncidentInvestigationService Service => new(
        Context, Access, User, Audit, _queue, NullLogger<IncidentInvestigationService>.Instance);

    private async Task<Guid> CreateIncidentAsync(string title = "HTTP 401 after JWT rotation")
    {
        var projectId = await CreateProjectAsync();
        var incident = await Incidents.CreateAsync(new CreateIncidentRequest
        {
            ProjectId = projectId,
            Title = title,
            Severity = IncidentSeverity.Sev1,
            Status = IncidentStatus.Open,
            Environment = "production",
            Summary = "Auth requests started failing after signing-key rotation.",
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

        return incident.Id;
    }

    // ── submit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Engineer_SubmitsInvestigation_Returns202_AndQueues()
    {
        var incidentId = await CreateIncidentAsync();
        var engineer = FakeCurrentUser.Standard();
        var projectId = Context.Set<ChangeLens.Domain.Incidents.Incident>().Single(i => i.Id == incidentId).ProjectId;
        await Projects.AddMemberAsync(projectId, engineer.UserId, "eng@test.dev", "Eng", ProjectRole.Engineer, CancellationToken.None);
        User = engineer;

        var response = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);

        Assert.Equal(AnalysisStatus.Queued, response.Status);
        Assert.Equal($"/api/v1/analyses/{response.AnalysisId}", response.StatusUrl);
        Assert.NotEqual(Guid.Empty, response.AnalysisId);

        var job = Assert.Single(_queue.Accepted);
        Assert.Equal(response.AnalysisId, job.AnalysisRunId);
        Assert.Equal(incidentId, job.IncidentId);

        var run = Context.Set<AnalysisRun>().Single(r => r.Id == response.AnalysisId);
        Assert.Equal("IncidentInvestigation", run.Type);
        Assert.Equal(AnalysisStatus.Queued, run.Status);
        Assert.Equal(incidentId, run.IncidentId);
        Assert.NotNull(run.QueuedAtUtc);
        Assert.Null(run.StartedAtUtc);
    }

    [Fact]
    public async Task Submission_IsAudited_AsAnalysisRequested()
    {
        var incidentId = await CreateIncidentAsync();

        await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest { RequestId = "key-1" }, CancellationToken.None);

        var audit = Context.Set<AuditLog>()
            .Where(a => a.Action == AuditActions.AnalysisRequested)
            .ToList();
        Assert.Single(audit);
        Assert.Equal(User.UserId, audit[0].UserId);
    }

    [Fact]
    public async Task NonMember_Submit_Gets404_AndNeverQueues()
    {
        var incidentId = await CreateIncidentAsync();
        var outsider = FakeCurrentUser.Standard();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => new IncidentInvestigationService(
                Context, Access, outsider, Audit, _queue, NullLogger<IncidentInvestigationService>.Instance)
                .SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None));

        Assert.Equal("not_found", ex.Code);
        Assert.Empty(_queue.Accepted);
    }

    [Fact]
    public async Task Viewer_Submit_Gets403_AndNeverQueues()
    {
        var incidentId = await CreateIncidentAsync();
        var projectId = Context.Set<ChangeLens.Domain.Incidents.Incident>().Single(i => i.Id == incidentId).ProjectId;
        var viewer = FakeCurrentUser.Standard();
        await Projects.AddMemberAsync(projectId, viewer.UserId, "viewer@test.dev", "Viewer", ProjectRole.Viewer, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => new IncidentInvestigationService(
                Context, Access, viewer, Audit, _queue, NullLogger<IncidentInvestigationService>.Instance)
                .SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None));

        Assert.Equal("forbidden", ex.Code);
        Assert.Empty(_queue.Accepted);
    }

    [Fact]
    public async Task SameRequestId_ReusesOutstandingRun()
    {
        var incidentId = await CreateIncidentAsync();
        var first = await Service.SubmitAsync(
            incidentId, new InvestigateIncidentRequest { RequestId = "key-1" }, CancellationToken.None);

        var second = await Service.SubmitAsync(
            incidentId, new InvestigateIncidentRequest { RequestId = "key-1" }, CancellationToken.None);

        Assert.Equal(first.AnalysisId, second.AnalysisId);
        Assert.Single(Context.Set<AnalysisRun>()); // no duplicate run
        Assert.Single(_queue.Accepted); // enqueued exactly once
    }

    [Fact]
    public async Task SameRequestId_AfterTerminalState_StartsFreshRun()
    {
        var incidentId = await CreateIncidentAsync();
        var first = await Service.SubmitAsync(
            incidentId, new InvestigateIncidentRequest { RequestId = "key-1" }, CancellationToken.None);

        // Mark the first run terminal (as the worker would).
        var run = Context.Set<AnalysisRun>().Single(r => r.Id == first.AnalysisId);
        run.Status = AnalysisStatus.Succeeded;
        await Context.SaveChangesAsync(CancellationToken.None);

        var second = await Service.SubmitAsync(
            incidentId, new InvestigateIncidentRequest { RequestId = "key-1" }, CancellationToken.None);

        Assert.NotEqual(first.AnalysisId, second.AnalysisId);
        Assert.Equal(2, Context.Set<AnalysisRun>().Count());
        Assert.Equal(2, _queue.Accepted.Count);
    }

    [Fact]
    public async Task QueueFull_MarksRunFailed_QueueFull_ButStillReturns202()
    {
        var incidentId = await CreateIncidentAsync();
        _queue.Accept = false;

        var response = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);

        Assert.Equal(AnalysisStatus.Queued, response.Status); // the 202 contract is unchanged
        var run = Context.Set<AnalysisRun>().Single(r => r.Id == response.AnalysisId);
        Assert.Equal(AnalysisStatus.Failed, run.Status);
        Assert.Equal(AnalysisFailureCode.QueueFull, run.FailureCode);
    }

    // ── status ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ReturnsSucceededResult()
    {
        var incidentId = await CreateIncidentAsync();
        var submitted = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);
        var run = Context.Set<AnalysisRun>().Single(r => r.Id == submitted.AnalysisId);
        run.Status = AnalysisStatus.Succeeded;
        run.ResultJson = """{"rootCauseCandidates":[],"remediation":{"insufficientEvidence":true},"unknowns":[],"evidence":[]}""";
        run.ResultSchemaVersion = "incident-v1";
        run.Model = "mock";
        run.PromptVersion = "incident-v1";
        run.CompletedAtUtc = DateTime.UtcNow;
        await Context.SaveChangesAsync(CancellationToken.None);

        var status = await Service.GetStatusAsync(submitted.AnalysisId, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Succeeded, status.Status);
        Assert.Equal("incident-v1", status.ResultSchemaVersion);
        Assert.Equal("mock", status.Model);
        Assert.NotNull(status.Result);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task GetStatus_ReturnsSafeError_WhenFailed()
    {
        var incidentId = await CreateIncidentAsync();
        var submitted = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);
        var run = Context.Set<AnalysisRun>().Single(r => r.Id == submitted.AnalysisId);
        run.Status = AnalysisStatus.Failed;
        run.FailureCode = AnalysisFailureCode.LlmRateLimited;
        run.Error = "The AI provider is rate limited.";
        await Context.SaveChangesAsync(CancellationToken.None);

        var status = await Service.GetStatusAsync(submitted.AnalysisId, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Failed, status.Status);
        Assert.NotNull(status.Error);
        Assert.Equal(AnalysisFailureCode.LlmRateLimited, status.Error.Code);
        Assert.Null(status.Result);
    }

    [Fact]
    public async Task GetStatus_NonMember_Gets404()
    {
        var incidentId = await CreateIncidentAsync();
        var submitted = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);
        var outsider = FakeCurrentUser.Standard();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => new IncidentInvestigationService(
                Context, Access, outsider, Audit, _queue, NullLogger<IncidentInvestigationService>.Instance)
                .GetStatusAsync(submitted.AnalysisId, CancellationToken.None));

        Assert.Equal("not_found", ex.Code);
    }

    [Fact]
    public async Task GetStatus_UnknownAnalysis_Gets404()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.GetStatusAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ── trace (Phase 7 observability) ─────────────────────────────────────────

    [Fact]
    public async Task GetTrace_ReturnsStagesRetrievalAndFailureCategory()
    {
        var incidentId = await CreateIncidentAsync();
        var submitted = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);
        var run = Context.Set<AnalysisRun>().Single(r => r.Id == submitted.AnalysisId);
        run.Status = AnalysisStatus.Succeeded;
        run.Model = "mock";
        run.PromptVersion = "incident-v1";
        run.ResultSchemaVersion = "incident-v1";
        run.TraceSchemaVersion = "trace-v1";
        run.TraceJson = """
        {"schemaVersion":"trace-v1","totalDurationMs":120,"stages":[
          {"name":"Context","status":"Completed","durationMs":5},
          {"name":"AI Analysis","status":"Completed","durationMs":95},
          {"name":"Persistence","status":"Completed","durationMs":10}
        ],"retrieval":{"queries":["HTTP 401 after JWT signing-key rotation"],"candidateCount":3,"selectedCount":2,"maxChunks":20,"maxCharsPerChunk":12000,"items":[
          {"id":"chunk:abc","documentType":"Runbook","path":"auth-001-jwt-key-rotation.md","keywordRank":1}
        ]},"failure":null}
        """;
        await Context.SaveChangesAsync(CancellationToken.None);

        var trace = await Service.GetTraceAsync(submitted.AnalysisId, CancellationToken.None);

        Assert.Equal(submitted.AnalysisId, trace.AnalysisId);
        Assert.Equal("trace-v1", trace.TraceSchemaVersion);
        Assert.Equal("mock", trace.Model);
        Assert.Equal(3, trace.Stages.Count);
        Assert.Contains(trace.Stages, s => s.Name == "Context" && s.DurationMs == 5);
        Assert.NotNull(trace.Retrieval);
        Assert.Equal(2, trace.Retrieval!.SelectedCount);
        Assert.Equal("chunk:abc", trace.Retrieval.Items[0].Id);
        Assert.Equal(1, trace.Retrieval.Items[0].KeywordRank);
        Assert.Null(trace.FailureCode);
        Assert.Null(trace.FailureCategory);
    }

    [Fact]
    public async Task GetTrace_FailedRun_MapsFailureCategory()
    {
        var incidentId = await CreateIncidentAsync();
        var submitted = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);
        var run = Context.Set<AnalysisRun>().Single(r => r.Id == submitted.AnalysisId);
        run.Status = AnalysisStatus.Failed;
        run.FailureCode = AnalysisFailureCode.LlmRateLimited;
        run.Error = "The AI provider is rate limited.";
        run.TraceSchemaVersion = "trace-v1";
        run.TraceJson = """{"schemaVersion":"trace-v1","stages":[],"retrieval":null,"failure":null}""";
        await Context.SaveChangesAsync(CancellationToken.None);

        var trace = await Service.GetTraceAsync(submitted.AnalysisId, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Failed, trace.Status);
        Assert.Equal(AnalysisFailureCode.LlmRateLimited, trace.FailureCode);
        Assert.Equal(AnalysisFailureCategory.RateLimit, trace.FailureCategory);
    }

    [Fact]
    public async Task GetTrace_NonMember_Gets404()
    {
        var incidentId = await CreateIncidentAsync();
        var submitted = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest(), CancellationToken.None);
        var outsider = FakeCurrentUser.Standard();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => new IncidentInvestigationService(
                Context, Access, outsider, Audit, _queue, NullLogger<IncidentInvestigationService>.Instance)
                .GetTraceAsync(submitted.AnalysisId, CancellationToken.None));

        Assert.Equal("not_found", ex.Code);
    }

    [Fact]
    public async Task GetTrace_UnknownAnalysis_Gets404()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.GetTraceAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ── list (Phase 6: dashboard + trace views) ───────────────────────────────

    [Fact]
    public async Task List_ReturnsOnlyTheProjectsRuns_WithFilters()
    {
        var incidentId = await CreateIncidentAsync();
        var projectId = Context.Set<ChangeLens.Domain.Incidents.Incident>().Single(i => i.Id == incidentId).ProjectId;

        var queued = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest { RequestId = "k-1" }, CancellationToken.None);
        var failed = await Service.SubmitAsync(incidentId, new InvestigateIncidentRequest { RequestId = "k-2" }, CancellationToken.None);

        var runQueued = Context.Set<AnalysisRun>().Single(r => r.Id == queued.AnalysisId);
        runQueued.Status = AnalysisStatus.Succeeded;
        runQueued.Model = "mock";
        runQueued.ResultSchemaVersion = "incident-v1";

        var runFailed = Context.Set<AnalysisRun>().Single(r => r.Id == failed.AnalysisId);
        runFailed.Status = AnalysisStatus.Failed;
        runFailed.FailureCode = AnalysisFailureCode.LlmRateLimited;
        runFailed.Error = "The AI provider is rate limited.";
        await Context.SaveChangesAsync(CancellationToken.None);

        // All runs for the project.
        var all = await Service.ListAsync(projectId, null, null, null, 1, 20, CancellationToken.None);
        Assert.Equal(2, all.Total);

        // Status filter.
        var succeeded = await Service.ListAsync(projectId, null, AnalysisStatus.Succeeded, null, 1, 20, CancellationToken.None);
        Assert.Equal(1, succeeded.Total);
        Assert.Equal(AnalysisStatus.Succeeded, succeeded.Items[0].Status);
        Assert.Equal("mock", succeeded.Items[0].Model);

        // Type + incident filter.
        var incidentFiltered = await Service.ListAsync(projectId, "IncidentInvestigation", null, incidentId, 1, 20, CancellationToken.None);
        Assert.Equal(2, incidentFiltered.Total);

        var wrongType = await Service.ListAsync(projectId, "ChangeRisk", null, null, 1, 20, CancellationToken.None);
        Assert.Equal(0, wrongType.Total);

        // Failed items expose the safe error representation.
        var failedItem = all.Items.Single(i => i.Id == failed.AnalysisId);
        Assert.Equal(AnalysisStatus.Failed, failedItem.Status);
        Assert.NotNull(failedItem.Error);
        Assert.Equal(AnalysisFailureCode.LlmRateLimited, failedItem.Error!.Code);
    }

    [Fact]
    public async Task List_NonMember_Gets404()
    {
        var incidentId = await CreateIncidentAsync();
        var projectId = Context.Set<ChangeLens.Domain.Incidents.Incident>().Single(i => i.Id == incidentId).ProjectId;
        var outsider = FakeCurrentUser.Standard();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => new IncidentInvestigationService(
                Context, Access, outsider, Audit, _queue, NullLogger<IncidentInvestigationService>.Instance)
                .ListAsync(projectId, null, null, null, 1, 20, CancellationToken.None));

        Assert.Equal("not_found", ex.Code);
    }

    private sealed class FakeJobQueue : IAnalysisJobQueue
    {
        public bool Accept { get; set; } = true;

        public List<AnalysisJob> Accepted { get; } = [];

        public bool TryEnqueue(AnalysisJob job)
        {
            if (!Accept)
            {
                return false;
            }

            Accepted.Add(job);
            return true;
        }

        public void Complete()
        {
        }
    }
}
