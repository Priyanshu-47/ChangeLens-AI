using System.Text.Json;
using ChangeLens.Application.Configuration;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Analysis;
using ChangeLens.Domain.Audit;
using ChangeLens.Domain.Incidents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChangeLens.Application.Services;

/// <summary>
/// Worker-side incident investigation (ADR-0009, brief §2/§24–27). Executes one job:
///
///   Queued → Running → (incident context → AI service hybrid retrieval → grounded
///   investigation) → Succeeded | Failed
///
/// The state machine is enforced both here and in <see cref="AnalysisRun.TransitionTo"/>
/// (a stale/double-enqueued job can never move a run backwards). Transient AI failures
/// (429/504/502) are retried with bounded exponential backoff; 422 validation failures
/// are never retried (retrying cannot repair the model's output). The per-job timeout is
/// a linked CTS, so an analysis can never remain Running forever; on host shutdown the
/// run is marked Failed(WORKER_INTERRUPTED) best-effort.
/// </summary>
public sealed class IncidentInvestigationOrchestrator(
    IAppDbContext db,
    IAiServiceClient aiClient,
    AuditLogService audit,
    IOptions<AnalysisOptions> options,
    ILogger<IncidentInvestigationOrchestrator> logger)
{
    // Results are stored camelCase so GET /analyses/{id} returns the AI-service shape
    // verbatim (the controller's JsonElement pass-through preserves the casing).
    private static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web);

    private AnalysisOptions Opts => options.Value;

    public async Task RunAsync(AnalysisJob job, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Opts.JobTimeoutSeconds)));
        var jobCt = timeout.Token;

        try
        {
            await ExecuteCoreAsync(job, jobCt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Per-job timeout, not host shutdown: fail the run explicitly.
            logger.LogWarning(
                "Analysis job {AnalysisRunId} exceeded the {Timeout}s job timeout",
                job.AnalysisRunId, Opts.JobTimeoutSeconds);
            await MarkFailedAsync(
                job, AnalysisFailureCode.JobTimeout,
                "The analysis exceeded the configured job timeout.", ct);
        }
        catch (AiServiceException ex)
        {
            await MarkFailedAsync(job, FailureCodeFor(ex), SafeMessage(ex), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Analysis job {AnalysisRunId} failed unexpectedly", job.AnalysisRunId);
            await MarkFailedAsync(job, AnalysisFailureCode.Internal,
                "The analysis failed unexpectedly.", ct);
        }
    }

    private async Task ExecuteCoreAsync(AnalysisJob job, CancellationToken ct)
    {
        var run = await db.Set<AnalysisRun>().FirstOrDefaultAsync(r => r.Id == job.AnalysisRunId, ct);
        if (run is null)
        {
            logger.LogWarning("Analysis run {AnalysisRunId} not found; job dropped", job.AnalysisRunId);
            return;
        }

        // Idempotent start: only a Queued run may begin. A duplicate enqueue or a
        // recovered job that already completed simply exits (no state corruption).
        if (run.Status != AnalysisStatus.Queued)
        {
            logger.LogInformation(
                "Analysis run {AnalysisRunId} is {Status}; skipping (not Queued)", run.Id, run.Status);
            return;
        }

        run.TransitionTo(AnalysisStatus.Running);
        run.StartedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.AnalysisStarted, "analysis", null, run.ProjectId, run.Id,
            details: new { analysisRunId = run.Id, analysisType = run.Type }, ct: ct);

        var response = await RunWithRetriesAsync(job, run, ct);

        run.TransitionTo(AnalysisStatus.Succeeded);
        run.ResultJson = JsonSerializer.Serialize(response.Result, ResultJson);
        run.ResultSchemaVersion = "incident-v1";
        run.Model = response.Usage.Model;
        run.PromptVersion = response.Usage.PromptVersion;
        run.RetrievalConfig = RetrievalSnapshot(response);
        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            AuditActions.AnalysisCompleted, "analysis", null, run.ProjectId, run.Id,
            details: new
            {
                analysisRunId = run.Id,
                model = response.Usage.Model,
                promptVersion = response.Usage.PromptVersion,
                validationStatus = response.Usage.ValidationStatus,
                latencyMs = response.Usage.LatencyMs,
                candidates = response.Result.RootCauseCandidates.Count,
                evidenceItems = response.Result.Evidence.Count,
                unknowns = response.Result.Unknowns.Count
            },
            ct: ct);

        logger.LogInformation(
            "Incident analysis completed for run {AnalysisRunId} (project {ProjectId}): " +
            "{Candidates} root-cause candidates, {Evidence} evidence items, {Unknowns} unknowns",
            run.Id, run.ProjectId,
            response.Result.RootCauseCandidates.Count, response.Result.Evidence.Count,
            response.Result.Unknowns.Count);
    }

    private async Task<IncidentAnalysisResponseDto> RunWithRetriesAsync(
        AnalysisJob job, AnalysisRun run, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, Opts.MaxRetries + 1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var incident = await db.Set<Incident>().AsNoTracking()
                    .Include(i => i.Events)
                    .FirstAsync(i => i.Id == job.IncidentId, ct);

                var request = new IncidentAnalysisRequestDto
                {
                    AnalysisId = run.Id,
                    ProjectId = run.ProjectId,
                    Incident = IncidentContextBuilder.Build(incident),
                    PromptVersion = "incident-v1"
                };

                return await aiClient.AnalyzeIncidentAsync(request, ct);
            }
            catch (AiServiceException ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                var delay = Backoff(attempt);
                logger.LogWarning(
                    "Transient AI failure ({Code}) on attempt {Attempt}/{MaxAttempts}; " +
                    "retrying in {DelaySeconds}s for run {AnalysisRunId}",
                    ex.Code, attempt, maxAttempts, delay.TotalSeconds, run.Id);
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task MarkFailedAsync(
        AnalysisJob job, string failureCode, string message, CancellationToken ct)
    {
        try
        {
            var run = await db.Set<AnalysisRun>().FirstOrDefaultAsync(r => r.Id == job.AnalysisRunId, ct);
            if (run is null)
            {
                return;
            }

            // A run already Succeeded/Failed (e.g. duplicate job) must not be overwritten.
            if (run.Status is AnalysisStatus.Succeeded or AnalysisStatus.Failed)
            {
                logger.LogWarning(
                    "Analysis run {AnalysisRunId} is already terminal ({Status}); " +
                    "ignoring late failure {Code}", run.Id, run.Status, failureCode);
                return;
            }

            if (run.Status == AnalysisStatus.Queued)
            {
                run.TransitionTo(AnalysisStatus.Running); // keep the state machine linear
                run.StartedAtUtc ??= DateTime.UtcNow;
            }

            run.TransitionTo(AnalysisStatus.Failed);
            run.FailureCode = failureCode;
            run.Error = message;
            run.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await audit.WriteAsync(
                AuditActions.AnalysisFailed, "analysis", null, run.ProjectId, run.Id,
                details: new { analysisRunId = run.Id, failureCode, message }, ct: ct);

            logger.LogWarning(
                "Analysis run {AnalysisRunId} failed: {Code} — {Message}",
                run.Id, failureCode, message);
        }
        catch (Exception ex)
        {
            // Failure handling must never throw into the worker loop (best-effort).
            logger.LogError(ex,
                "Could not persist failure for analysis run {AnalysisRunId}", job.AnalysisRunId);
        }
    }

    private static bool IsTransient(AiServiceException ex)
        => ex is AiRateLimitedException or AiTimeoutException or AiUnavailableException;

    private static string FailureCodeFor(AiServiceException ex) => ex switch
    {
        AiValidationFailedException => AnalysisFailureCode.AiValidationFailed,
        AiRateLimitedException => AnalysisFailureCode.LlmRateLimited,
        AiTimeoutException => AnalysisFailureCode.AiTimeout,
        AiUnavailableException => AnalysisFailureCode.AiUnavailable,
        _ => AnalysisFailureCode.Internal
    };

    private static string SafeMessage(AiServiceException ex)
    {
        // The AI client already returns sanitized messages; cap defensively anyway.
        var message = ex.Message ?? "The AI analysis failed.";
        return message.Length <= 500 ? message : message[..500];
    }

    private TimeSpan Backoff(int attempt)
    {
        var baseSeconds = Math.Max(1, Opts.RetryBackoffSeconds);
        var seconds = Math.Min(30, baseSeconds * (int)Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string? RetrievalSnapshot(IncidentAnalysisResponseDto response)
    {
        var config = JsonSerializer.Serialize(new
        {
            promptVersion = response.Usage.PromptVersion,
            validationStatus = response.Usage.ValidationStatus,
            evidenceTruncated = response.Usage.EvidenceTruncated,
            candidateCount = response.Result.RootCauseCandidates.Count
        });
        return config;
    }
}
