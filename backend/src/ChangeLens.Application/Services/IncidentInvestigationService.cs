using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Analysis;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Incidents;
using Microsoft.Extensions.Logging;

namespace ChangeLens.Application.Services;

/// <summary>
/// Public API for the async incident workflow (api-contract.md §5, ADR-0009):
///
///   POST /api/v1/incidents/{incidentId}/investigate → 202 { analysisId, status, statusUrl }
///   GET  /api/v1/analyses/{analysisId}              → 200 { status, result?, error? }
///
/// Project authorization is enforced here (Write to submit, Read to poll); the AI
/// service never sees user tokens. Idempotency: a client-supplied RequestId reuses an
/// outstanding (Queued/Running) run for the same project; after a terminal state a new
/// submission starts a fresh run (the unique index only covers non-terminal statuses).
/// A full bounded queue is not silently dropped: the run is persisted as Failed(QUEUE_FULL)
/// and the 202 still returns its id so polling surfaces the terminal state honestly.
/// </summary>
public sealed class IncidentInvestigationService(
    IAppDbContext db,
    ProjectAccessService access,
    ICurrentUser currentUser,
    AuditLogService audit,
    IAnalysisJobQueue queue,
    ILogger<IncidentInvestigationService> logger)
{
    public async Task<InvestigationAcceptedResponse> SubmitAsync(
        Guid incidentId, InvestigateIncidentRequest request, CancellationToken ct)
    {
        var incident = await db.Set<Incident>().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new NotFoundException("Incident not found.");

        // Engineer (or project role with Write) may submit an investigation; Viewers
        // cannot (api-contract.md §4).
        await access.RequireAsync(
            incident.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin,
            ProjectPermission.Write, ct);

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? null
            : request.RequestId.Trim();

        // Idempotency (brief §8): reuse an outstanding run for the same key.
        if (requestId is not null)
        {
            var existing = await db.Set<AnalysisRun>().AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.ProjectId == incident.ProjectId && r.RequestId == requestId, ct);

            if (existing is { Status: AnalysisStatus.Queued or AnalysisStatus.Running })
            {
                logger.LogInformation(
                    "Reusing outstanding investigation run {AnalysisRunId} for request key {RequestId}",
                    existing.Id, requestId);
                return AcceptedResponse(existing.Id);
            }
        }

        var run = new AnalysisRun
        {
            Id = Guid.NewGuid(),
            ProjectId = incident.ProjectId,
            Type = "IncidentInvestigation",
            Status = AnalysisStatus.Queued,
            IncidentId = incident.Id,
            RequestId = requestId,
            QueuedAtUtc = DateTime.UtcNow,
            RetrievalConfig = JsonSerializer.Serialize(new { retrieval = "hybrid-rrf", evidenceBudget = "server" })
        };
        db.Set<AnalysisRun>().Add(run);
        await db.SaveChangesAsync(ct);

        var accepted = queue.TryEnqueue(new AnalysisJob(run.Id, run.ProjectId, incident.Id, requestId));
        if (!accepted)
        {
            // Bounded queue is full — never lose the job silently. Persist the terminal
            // state; the 202 still returns the id so the client can poll it.
            run.TransitionTo(AnalysisStatus.Failed);
            run.FailureCode = AnalysisFailureCode.QueueFull;
            run.Error = "The analysis queue is full; retry shortly.";
            run.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Analysis queue full; investigation run {AnalysisRunId} marked QUEUE_FULL",
                run.Id);
        }

        await audit.WriteAsync(
            AuditActions.AnalysisRequested, "analysis", currentUser.UserId, run.ProjectId, run.Id,
            currentUser.IpAddress,
            new { analysisRunId = run.Id, incidentId = incident.Id, analysisType = run.Type, requestId },
            ct);

        return AcceptedResponse(run.Id);
    }

    public async Task<AnalysisStatusResponse> GetStatusAsync(Guid analysisId, CancellationToken ct)
    {
        var run = await db.Set<AnalysisRun>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == analysisId, ct)
            ?? throw new NotFoundException("Analysis not found.");

        // Non-members see 404 (existence is not revealed); members with Read may poll.
        await access.RequireAsync(
            run.ProjectId, currentUser.UserId, currentUser.IsGlobalAdmin,
            ProjectPermission.Read, ct);

        return new AnalysisStatusResponse
        {
            Id = run.Id,
            ProjectId = run.ProjectId,
            Type = run.Type,
            Status = run.Status,
            IncidentId = run.IncidentId,
            Result = run.ResultJson is null
                ? null
                : JsonSerializer.Deserialize<JsonElement>(run.ResultJson),
            ResultSchemaVersion = run.ResultSchemaVersion,
            Model = run.Model,
            PromptVersion = run.PromptVersion,
            QueuedAtUtc = run.QueuedAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            Error = run.FailureCode is null && run.Error is null
                ? null
                : new AnalysisErrorDto { Code = run.FailureCode, Message = run.Error }
        };
    }

    private static InvestigationAcceptedResponse AcceptedResponse(Guid analysisId) => new()
    {
        AnalysisId = analysisId,
        Status = AnalysisStatus.Queued,
        StatusUrl = $"/api/v1/analyses/{analysisId}"
    };
}
