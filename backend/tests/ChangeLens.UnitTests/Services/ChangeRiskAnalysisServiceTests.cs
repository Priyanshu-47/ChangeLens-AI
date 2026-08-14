using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Services;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Projects;
using ChangeLens.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChangeLens.UnitTests.Services;

public sealed class ChangeRiskAnalysisServiceTests : ServiceTestBase
{
    private readonly FakeAiClient _ai = new();

    private ChangeRiskAnalysisService Service => new(
        Access, User, Audit, _ai, NullLogger<ChangeRiskAnalysisService>.Instance);

    private static AnalyzeChangeRiskRequest Request(Guid projectId) => new()
    {
        ProjectId = projectId,
        ChangeSummary = "Changed token refresh logic.",
        ChangedFiles = [new ChangedFileRequest { Path = "src/AuthClient.cs" }]
    };

    [Fact]
    public async Task EmptyProjectId_IsRejectedBeforeAnyCall()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service.AnalyzeChangeRiskAsync(Request(Guid.Empty), CancellationToken.None));

        Assert.Equal("validation_failed", ex.Code);
        Assert.False(_ai.WasCalled);
    }

    [Fact]
    public async Task NonMember_Gets404_AndAiIsNeverCalled()
    {
        var projectId = await CreateProjectAsync();
        var outsider = FakeCurrentUser.Standard();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => new ChangeRiskAnalysisService(
                Access, outsider, Audit, _ai, NullLogger<ChangeRiskAnalysisService>.Instance)
                .AnalyzeChangeRiskAsync(Request(projectId), CancellationToken.None));

        Assert.Equal("not_found", ex.Code);
        Assert.False(_ai.WasCalled);
    }

    [Fact]
    public async Task ViewerMember_Gets403_AndAiIsNeverCalled()
    {
        var projectId = await CreateProjectAsync();
        var viewer = FakeCurrentUser.Standard();
        await Projects.AddMemberAsync(projectId, viewer.UserId, "viewer@test.dev", "Viewer", ProjectRole.Viewer, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => new ChangeRiskAnalysisService(
                Access, viewer, Audit, _ai, NullLogger<ChangeRiskAnalysisService>.Instance)
                .AnalyzeChangeRiskAsync(Request(projectId), CancellationToken.None));

        Assert.Equal("forbidden", ex.Code);
        Assert.False(_ai.WasCalled);
    }

    [Fact]
    public async Task EngineerMember_RunsAnalysis_AndAudits()
    {
        var projectId = await CreateProjectAsync();
        var engineer = FakeCurrentUser.Standard();
        await Projects.AddMemberAsync(projectId, engineer.UserId, "eng@test.dev", "Eng", ProjectRole.Engineer, CancellationToken.None);
        User = engineer;

        var response = await Service.AnalyzeChangeRiskAsync(Request(projectId), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(projectId, _ai.Received!.ProjectId);
        Assert.Equal("MEDIUM", response.Result.RiskLevel);

        var audit = Context.Set<AuditLog>()
            .Where(a => a.Action == AuditActions.AnalysisRequested && a.ProjectId == projectId)
            .ToList();
        Assert.Single(audit);
        Assert.Equal(engineer.UserId, audit[0].UserId);
    }

    [Fact]
    public async Task GlobalAdmin_BypassesMembership()
    {
        var projectId = await CreateProjectAsync();
        User = FakeCurrentUser.Admin();

        var response = await Service.AnalyzeChangeRiskAsync(Request(projectId), CancellationToken.None);

        Assert.Equal(projectId, _ai.Received!.ProjectId);
    }

    [Fact]
    public async Task AiValidationFailure_Propagates()
    {
        var projectId = await CreateProjectAsync();
        _ai.ExceptionToThrow = new AiValidationFailedException("AI output failed validation after bounded repair.");

        var ex = await Assert.ThrowsAsync<AiValidationFailedException>(
            () => Service.AnalyzeChangeRiskAsync(Request(projectId), CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
    }

    private sealed class FakeAiClient : IAiServiceClient
    {
        public AnalyzeChangeRiskRequest? Received { get; private set; }

        public bool WasCalled => Received is not null;

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
                Result = new ChangeRiskResultDto { RiskLevel = "MEDIUM", Confidence = 0.7 },
                Usage = new AnalysisUsageDto { Model = "mock", ValidationStatus = "valid" }
            });
        }
    }
}
