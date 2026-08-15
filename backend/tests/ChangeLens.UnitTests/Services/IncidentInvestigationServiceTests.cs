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
