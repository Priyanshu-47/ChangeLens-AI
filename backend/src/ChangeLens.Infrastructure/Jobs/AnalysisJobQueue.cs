using System.Threading.Channels;
using ChangeLens.Application.Configuration;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;
using Microsoft.Extensions.Options;

namespace ChangeLens.Infrastructure.Jobs;

/// <summary>
/// Bounded, in-process job queue (ADR-0009, brief §24). A <see cref="Channel{T}"/>
/// with a bounded capacity: when full, <see cref="TryEnqueue"/> returns false and the
/// caller decides the failure path (QUEUE_FULL) instead of silently unbounded buffering.
/// No Redis/Kafka/SQS — the MVP is $0-first and single-instance.
/// </summary>
public sealed class AnalysisJobQueue : IAnalysisJobQueue
{
    private readonly Channel<AnalysisJob> _channel;

    public AnalysisJobQueue(IOptions<AnalysisOptions> options)
    {
        var capacity = Math.Max(1, options.Value.QueueCapacity);
        _channel = Channel.CreateBounded<AnalysisJob>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // TryWrite still fails when full
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    /// <summary>True when the job was accepted; false when the bounded queue is full.</summary>
    public bool TryEnqueue(AnalysisJob job) => _channel.Writer.TryWrite(job);

    /// <summary>Signals graceful shutdown — no further jobs are accepted.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>Reader consumed by <see cref="AnalysisWorker"/> (testable via the channel).</summary>
    public ChannelReader<AnalysisJob> Reader => _channel.Reader;
}
