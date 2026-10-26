using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Services;

namespace Sufni.App.Tests.Sessions.Services;

public class SessionOperationRunnerTests
{
    private readonly SessionOperationRunner runner = new();
    private readonly Guid sessionId = Guid.NewGuid();

    [Fact]
    public void Report_PublishesProgressOfTheSession()
    {
        var states = Watch(sessionId);

        using var operation = runner.Start(sessionId, "matching");
        operation.Report("halfway", 50);

        Assert.Equal(
            [
                SessionOperationPresentationState.Hidden,
                SessionOperationPresentationState.Progress("matching", 0),
                SessionOperationPresentationState.Progress("halfway", 50),
            ],
            states);
    }

    [Fact]
    public void Start_CancelsThePreviousOperationOfTheSession()
    {
        var first = runner.Start(sessionId, "first");
        using var second = runner.Start(sessionId, "second");
        var states = Watch(sessionId);

        first.Report("stale", 20);
        first.Dispose();

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Equal([SessionOperationPresentationState.Progress("second", 0)], states);
    }

    [Fact]
    public void Dispose_HidesTheProgress()
    {
        var operation = runner.Start(sessionId, "matching");
        var states = Watch(sessionId);

        operation.Dispose();

        Assert.False(operation.IsCurrent);
        Assert.Equal(SessionOperationPresentationState.Hidden, states[^1]);
    }

    [Fact]
    public void Cancel_CancelsTheOperationAndHidesTheProgress()
    {
        var operation = runner.Start(sessionId, "matching");
        var states = Watch(sessionId);

        runner.Cancel(sessionId);

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        Assert.False(operation.IsCurrent);
        Assert.Equal(SessionOperationPresentationState.Hidden, states[^1]);
    }

    [Fact]
    public void Start_LeavesOperationsOfOtherSessionsRunning()
    {
        var otherSessionId = Guid.NewGuid();
        using var other = runner.Start(otherSessionId, "other");

        using var operation = runner.Start(sessionId, "matching");

        Assert.True(other.IsCurrent);
        Assert.False(other.CancellationToken.IsCancellationRequested);
    }

    private List<SessionOperationPresentationState> Watch(Guid id)
    {
        var states = new List<SessionOperationPresentationState>();
        runner.Watch(id).Subscribe(states.Add);
        return states;
    }
}
