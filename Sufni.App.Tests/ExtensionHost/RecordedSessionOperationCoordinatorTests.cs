using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHosting.RecordedSessions;

namespace Sufni.App.Tests.ExtensionHost;

public class RecordedSessionOperationCoordinatorTests
{
    [Fact]
    public void StartOperation_CancelsPreviousLeaseAndKeepsOnlyLatestCurrent()
    {
        var reports = new List<(string Message, double Percent)>();
        var completed = 0;
        var coordinator = new RecordedSessionOperationCoordinator(
            (message, percent) => reports.Add((message, percent)),
            () => completed++);

        var first = coordinator.StartOperation("first");
        var second = coordinator.StartOperation("second");

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Equal([("first", 0), ("second", 0)], reports);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void StaleLease_CannotReportOrCompleteCurrentOperation()
    {
        var reports = new List<(string Message, double Percent)>();
        var completed = 0;
        var coordinator = new RecordedSessionOperationCoordinator(
            (message, percent) => reports.Add((message, percent)),
            () => completed++);

        var stale = coordinator.StartOperation("stale");
        var current = coordinator.StartOperation("current");

        stale.Report("stale-progress", 50);
        stale.Complete();
        current.Report("current-progress", 25);

        Assert.True(current.IsCurrent);
        Assert.Equal([("stale", 0), ("current", 0), ("current-progress", 25)], reports);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void CurrentLease_CompleteClearsOperationOnce()
    {
        var completed = 0;
        var coordinator = new RecordedSessionOperationCoordinator(
            (_, _) => { },
            () => completed++);
        var lease = coordinator.StartOperation("current");

        lease.Complete();
        lease.Complete();

        Assert.False(lease.IsCurrent);
        Assert.Equal(1, completed);
    }
}
