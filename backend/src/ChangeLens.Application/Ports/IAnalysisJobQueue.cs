using ChangeLens.Application.Dtos;

namespace ChangeLens.Application.Ports;

/// <summary>
/// Bounded, in-process job queue for async analyses (ADR-0009, brief §24). The MVP
/// queue is a bounded <c>Channel&lt;AnalysisJob&gt;</c> — no Redis/Kafka/SQS. Enqueue
/// is non-blocking: when the queue is full the caller decides (the orchestrator marks
/// the run Failed with QUEUE_FULL rather than losing the job silently).
/// </summary>
public interface IAnalysisJobQueue
{
    /// <summary>True when the job was accepted by the bounded queue.</summary>
    bool TryEnqueue(AnalysisJob job);

    /// <summary>Signals graceful shutdown — no further jobs are accepted.</summary>
    void Complete();
}
