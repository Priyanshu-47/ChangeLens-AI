using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Audit;
using Microsoft.Extensions.Logging;

namespace ChangeLens.Application.Services;

/// <summary>
/// Phase 2 vertical slice: proves the .NET → FastAPI → Gemini path for a change-risk
/// analysis. User/project authorization stays here (the AI service never sees user
/// tokens); the AI client performs the internal-service authentication.
/// </summary>
public sealed class ChangeRiskAnalysisService(
    ProjectAccessService projectAccess,
    ICurrentUser currentUser,
    AuditLogService audit,
    IAiServiceClient aiClient,
    ILogger<ChangeRiskAnalysisService> logger)
{
    public async Task<ChangeRiskAnalysisResponse> AnalyzeChangeRiskAsync(
        AnalyzeChangeRiskRequest request, CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty)
        {
            throw new ValidationException("ProjectId is required.");
        }

        // Authorization matrix (docs/api-contract.md §4): Viewer cannot run analyses.
        await projectAccess.RequireAsync(
            request.ProjectId,
            currentUser.UserId,
            currentUser.IsGlobalAdmin,
            ProjectPermission.Write,
            ct);

        logger.LogInformation(
            "Change-risk analysis requested for project {ProjectId} by user {UserId}",
            request.ProjectId, currentUser.UserId);

        var response = await aiClient.AnalyzeChangeRiskAsync(request, ct);

        await audit.WriteAsync(
            AuditActions.AnalysisRequested,
            "analysis",
            currentUser.UserId,
            request.ProjectId,
            ipAddress: currentUser.IpAddress,
            details: new
            {
                model = response.Usage.Model,
                promptVersion = response.Usage.PromptVersion,
                validationStatus = response.Usage.ValidationStatus,
                latencyMs = response.Usage.LatencyMs
            },
            ct: ct);

        return response;
    }
}
