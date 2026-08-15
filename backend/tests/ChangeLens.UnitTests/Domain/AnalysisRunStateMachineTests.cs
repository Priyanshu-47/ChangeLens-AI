using ChangeLens.Domain.Analysis;

namespace ChangeLens.UnitTests.Domain;

/// <summary>Job state machine (api-contract.md §5.2, brief §4): Queued → Running → Succeeded | Failed.</summary>
public sealed class AnalysisRunStateMachineTests
{
    private static AnalysisRun NewRun(string status) => new() { Status = status };

    [Fact]
    public void Queued_CanTransition_ToRunning_AndFailed()
    {
        var run = NewRun(AnalysisStatus.Queued);
        run.TransitionTo(AnalysisStatus.Running);
        Assert.Equal(AnalysisStatus.Running, run.Status);

        var failed = NewRun(AnalysisStatus.Queued);
        failed.TransitionTo(AnalysisStatus.Failed);
        Assert.Equal(AnalysisStatus.Failed, failed.Status);
    }

    [Fact]
    public void Running_CanTransition_ToSucceeded_AndFailed()
    {
        var run = NewRun(AnalysisStatus.Running);
        run.TransitionTo(AnalysisStatus.Succeeded);
        Assert.Equal(AnalysisStatus.Succeeded, run.Status);

        var failed = NewRun(AnalysisStatus.Running);
        failed.TransitionTo(AnalysisStatus.Failed);
        Assert.Equal(AnalysisStatus.Failed, failed.Status);
    }

    [Theory]
    [InlineData(AnalysisStatus.Succeeded, AnalysisStatus.Running)]
    [InlineData(AnalysisStatus.Succeeded, AnalysisStatus.Queued)]
    [InlineData(AnalysisStatus.Succeeded, AnalysisStatus.Succeeded)]
    [InlineData(AnalysisStatus.Failed, AnalysisStatus.Running)]
    [InlineData(AnalysisStatus.Failed, AnalysisStatus.Succeeded)]
    [InlineData(AnalysisStatus.Failed, AnalysisStatus.Queued)]
    [InlineData(AnalysisStatus.Running, AnalysisStatus.Queued)]
    public void InvalidTransitions_AreRejected(string from, string to)
    {
        var run = NewRun(from);
        var ex = Assert.Throws<InvalidOperationException>(() => run.TransitionTo(to));
        Assert.Contains("Invalid analysis state transition", ex.Message);
    }

    [Fact]
    public void UnknownStatus_IsRejected()
    {
        var run = NewRun(AnalysisStatus.Queued);
        Assert.Throws<InvalidOperationException>(() => run.TransitionTo("COMPLETED"));
        Assert.Throws<InvalidOperationException>(() => run.TransitionTo(""));
    }
}
