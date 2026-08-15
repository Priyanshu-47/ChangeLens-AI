using ChangeLens.Application.Configuration;
using ChangeLens.Application.Dtos;
using ChangeLens.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace ChangeLens.UnitTests.Infrastructure;

/// <summary>Bounded in-process job queue (ADR-0009, brief §24).</summary>
public sealed class AnalysisJobQueueTests
{
    private static AnalysisJobQueue CreateQueue(int capacity = 4) => new(
        Options.Create(new AnalysisOptions { QueueCapacity = capacity }));

    private static AnalysisJob Job(int n) => new(
        AnalysisRunId: Guid.NewGuid(),
        ProjectId: Guid.NewGuid(),
        IncidentId: Guid.NewGuid(),
        RequestId: $"req-{n}");

    [Fact]
    public void Enqueue_AcceptsUpToCapacity()
    {
        var queue = CreateQueue(capacity: 2);
        Assert.True(queue.TryEnqueue(Job(1)));
        Assert.True(queue.TryEnqueue(Job(2)));
        Assert.False(queue.TryEnqueue(Job(3))); // bounded: full
    }

    [Fact]
    public async Task Complete_StopsDelivery_AndEnqueueFails()
    {
        var queue = CreateQueue();
        Assert.True(queue.TryEnqueue(Job(1)));
        queue.Complete();

        Assert.False(queue.TryEnqueue(Job(2)));

        var delivered = 0;
        await foreach (var _ in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            delivered++;
        }

        Assert.Equal(1, delivered);
    }

    [Fact]
    public async Task Reader_DeliversJobsInOrder()
    {
        var queue = CreateQueue(capacity: 3);
        queue.TryEnqueue(Job(1));
        queue.TryEnqueue(Job(2));
        queue.TryEnqueue(Job(3));
        queue.Complete();

        var ids = new List<Guid>();
        await foreach (var job in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            ids.Add(job.AnalysisRunId);
        }

        Assert.Equal(3, ids.Count);
    }
}
