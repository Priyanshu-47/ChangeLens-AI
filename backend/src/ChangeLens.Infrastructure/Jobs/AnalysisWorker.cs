using ChangeLens.Application.Configuration;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Services;
using ChangeLens.Domain.Analysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChangeLens.Infrastructure.Jobs;

/// <summary>
/// Background consumer for async analyses (ADR-0009, brief §24–25). Reads the bounded
/// queue with at most <c>MaxConcurrency</c> parallel consumers (configurable — never an
/// unbounded fan-out of AI calls) and runs each job through
/// <see cref="IncidentInvestigationOrchestrator"/> in its own DI scope. The per-job
/// timeout and state transitions live in the orchestrator, which owns the DB writes.
/// Graceful shutdown: the host cancels the token, pending jobs complete or are marked
/// interrupted, and in-flight AI calls are cancelled.
/// </summary>
public sealed class AnalysisWorker : BackgroundService
{
    private readonly AnalysisJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AnalysisOptions> _options;
    private readonly ILogger<AnalysisWorker> _logger;

    public AnalysisWorker(
        AnalysisJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<AnalysisOptions> options,
        ILogger<AnalysisWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Crash recovery (brief §27, AnalysisOptions.RecoverOnStartup): after an
        // unclean shutdown, stale Running runs are failed as WORKER_INTERRUPTED and
        // Queued runs that were persisted but never processed are re-enqueued — before
        // consumers start. Best-effort: recovery failures log and never block startup.
        if (_options.Value.RecoverOnStartup)
        {
            await RecoverInterruptedAsync(cancellationToken);
        }

        await base.StartAsync(cancellationToken);
    }

    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AnalysisWorker>>();

        try
        {
            var interrupted = await db.Set<AnalysisRun>()
                .Where(r => r.Status == AnalysisStatus.Running)
                .ToListAsync(ct);
            foreach (var run in interrupted)
            {
                run.TransitionTo(AnalysisStatus.Failed);
                run.FailureCode = AnalysisFailureCode.WorkerInterrupted;
                run.Error = "The worker was interrupted before the analysis completed.";
                run.CompletedAtUtc ??= DateTime.UtcNow;
                logger.LogWarning(
                    "Recovering interrupted analysis run {AnalysisRunId} -> Failed(WORKER_INTERRUPTED)",
                    run.Id);
            }
            await db.SaveChangesAsync(ct);

            var queued = await db.Set<AnalysisRun>()
                .Where(r => r.Status == AnalysisStatus.Queued)
                .ToListAsync(ct);
            var reenqueued = 0;
            foreach (var run in queued)
            {
                if (_queue.TryEnqueue(new AnalysisJob(run.Id, run.ProjectId, run.IncidentId ?? Guid.Empty, run.RequestId)))
                {
                    reenqueued++;
                }
            }

            if (interrupted.Count > 0 || reenqueued > 0)
            {
                logger.LogInformation(
                    "Startup recovery: {Interrupted} interrupted runs failed, {Reenqueued} queued runs re-enqueued",
                    interrupted.Count, reenqueued);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup recovery failed; continuing without it.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = Math.Max(1, _options.Value.MaxConcurrency);
        _logger.LogInformation(
            "Analysis worker started with concurrency {Concurrency} and queue capacity {Capacity}",
            concurrency, Math.Max(1, _options.Value.QueueCapacity));

        var consumers = Enumerable.Range(0, concurrency)
            .Select(_ => ConsumeAsync(stoppingToken))
            .ToArray();

        await Task.WhenAll(consumers);
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider
                .GetRequiredService<IncidentInvestigationOrchestrator>();

            try
            {
                await orchestrator.RunAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutdown: the orchestrator marks in-flight jobs as interrupted
                // via its own cancelled-token path; stop consuming.
                _logger.LogWarning(
                    "Analysis worker stopping; job {AnalysisRunId} was interrupted by shutdown",
                    job.AnalysisRunId);
                return;
            }
            catch (Exception ex)
            {
                // Orchestrator failures are persisted as Failed(INTERNAL) by design;
                // anything escaping it is a bug — never crash the host.
                _logger.LogError(ex,
                    "Unhandled error while running analysis job {AnalysisRunId}",
                    job.AnalysisRunId);
            }
        }
    }
}
