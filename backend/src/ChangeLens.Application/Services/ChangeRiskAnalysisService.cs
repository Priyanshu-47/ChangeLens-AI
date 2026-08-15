using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Tracing;
using ChangeLens.Domain.Analysis;
using ChangeLens.Domain.Audit;
using Microsoft.Extensions.Logging;

namespace ChangeLens.Application.Services;

/// <summary>
/// Phase 4 change-risk orchestration (docs/rag-architecture.md §12–26):
///
///   change request → Roslyn change analysis → dependency graph → enriched AI request
///   → AI service hybrid retrieval (vector + keyword + dependency, RRF) → grounded risk
///   report → persisted analysis_runs row + audit log.
///
/// User/project authorization stays here (the AI service never sees user tokens); the
/// AI client performs the internal-service authentication. Evidence is discovered
/// server-side — the client does not supply it (brief §22).
/// </summary>
public sealed class ChangeRiskAnalysisService(
    ProjectAccessService projectAccess,
    ICurrentUser currentUser,
    AuditLogService audit,
    IAiServiceClient aiClient,
    IChangeAnalysisEngine changeEngine,
    IAppDbContext db,
    ILogger<ChangeRiskAnalysisService> logger)
{
    private static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web);
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
            "Change-risk analysis started for project {ProjectId} by user {UserId}",
            request.ProjectId, currentUser.UserId);

        var trace = new AnalysisTraceBuilder();

        // 1. Roslyn change analysis: changed/impacted symbols, dependency graph,
        //    impacted APIs and external integrations (safe local git source).
        ChangeModelDto change;
        using (trace.Stage("Roslyn + Dependency Graph"))
        {
            change = changeEngine.BuildChangeModel(request);
        }
        request.ChangedFiles = change.ChangedFiles;
        request.ChangedSymbols = change.ChangedSymbols;
        request.ImpactedSymbols = change.ImpactedSymbols;
        request.DependencyEdges = change.DependencyEdges;
        request.DependencyPaths = change.DependencyPaths;
        request.ImpactedServices = change.ImpactedServices;

        // 2. Persist the analysis run (audit trail, ADR-0009) before the AI call so a
        //    failure is still recorded.
        var runId = Guid.NewGuid();
        request.AnalysisRunId = runId;
        var run = new AnalysisRun
        {
            Id = runId,
            ProjectId = request.ProjectId,
            Type = "ChangeRisk",
            Status = "Running",
            ChangeIdentifier = ChangeIdentifier(request),
            QueuedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow,
            RetrievalConfig = JsonSerializer.Serialize(new
            {
                changedSymbols = change.ChangedSymbols.Count,
                impactedSymbols = change.ImpactedSymbols.Count,
                dependencyPaths = change.DependencyPaths.Count,
                impactedApis = change.ImpactedApis.Count,
                warnings = change.Warnings.Count
            })
        };
        db.Set<AnalysisRun>().Add(run);
        await db.SaveChangesAsync(ct);

        ChangeRiskAnalysisResponse response;
        try
        {
            using (trace.Stage("AI Analysis"))
            {
                response = await aiClient.AnalyzeChangeRiskAsync(request, ct);
            }
            run.Status = "Succeeded";
            run.Model = response.Usage.Model;
            run.PromptVersion = response.Usage.PromptVersion;
            run.ResultJson = JsonSerializer.Serialize(response.Result, ResultJson);
            run.ResultSchemaVersion = "change-risk-v1";
            run.CompletedAtUtc = DateTime.UtcNow;
            trace.SetRetrieval(response.Trace);
        }
        catch (ChangeLensException ex)
        {
            run.Status = "Failed";
            run.FailureCode = FailureCodeFor(ex);
            run.Error = ex.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            trace.Fail(run.FailureCode, ex.Message);
            run.TraceJson = trace.Serialize();
            run.TraceSchemaVersion = trace.Schema;
            await db.SaveChangesAsync(ct);
            throw;
        }

        using (trace.Stage("Persistence"))
        {
            run.TraceJson = trace.Serialize();
            run.TraceSchemaVersion = trace.Schema;
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Change-risk analysis completed for project {ProjectId}: {Changed} changed, " +
            "{Impacted} impacted symbols, run {RunId}",
            request.ProjectId, change.ChangedSymbols.Count, change.ImpactedSymbols.Count, runId);

        await audit.WriteAsync(
            AuditActions.AnalysisRequested,
            "analysis",
            currentUser.UserId,
            request.ProjectId,
            ipAddress: currentUser.IpAddress,
            details: new
            {
                analysisRunId = runId,
                model = response.Usage.Model,
                promptVersion = response.Usage.PromptVersion,
                validationStatus = response.Usage.ValidationStatus,
                latencyMs = response.Usage.LatencyMs,
                changedSymbols = change.ChangedSymbols.Count,
                impactedSymbols = change.ImpactedSymbols.Count,
                impactedApis = change.ImpactedApis.Count,
                externalIntegrations = change.ExternalIntegrationImpacts.Count,
                analysisWarnings = change.Warnings
            },
            ct: ct);

        return new ChangeRiskAnalysisResponse
        {
            AnalysisType = response.AnalysisType,
            Result = response.Result,
            Usage = response.Usage,
            AnalysisRunId = runId
        };
    }

    private static string FailureCodeFor(ChangeLensException ex) => ex switch
    {
        AiValidationFailedException => AnalysisFailureCode.AiValidationFailed,
        AiRateLimitedException => AnalysisFailureCode.LlmRateLimited,
        AiTimeoutException => AnalysisFailureCode.AiTimeout,
        AiUnavailableException => AnalysisFailureCode.AiUnavailable,
        _ => AnalysisFailureCode.Internal
    };

    private static string ChangeIdentifier(AnalyzeChangeRiskRequest request)
    {
        if (request.BaseRevision is not null)
        {
            return $"{request.BaseRevision}..{request.TargetRevision ?? "working-tree"}";
        }

        return request.TargetRevision is not null
            ? $"HEAD..{request.TargetRevision}"
            : "provided-change";
    }
}
