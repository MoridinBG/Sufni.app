using Sufni.App.Sessions.Coordination;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Coordination;

public class SessionWindowEditPolicyTests
{
    [Theory]
    [InlineData(SessionWindowEditKind.TrimFromBeginning, 0.05, false)]
    [InlineData(SessionWindowEditKind.TrimFromBeginning, 0.1, true)]
    [InlineData(SessionWindowEditKind.TrimFromBeginning, 0.5, true)]
    [InlineData(SessionWindowEditKind.TrimFromBeginning, 0.6, false)]
    [InlineData(SessionWindowEditKind.TrimUntilEnd, 0.4, false)]
    [InlineData(SessionWindowEditKind.TrimUntilEnd, 0.5, true)]
    [InlineData(SessionWindowEditKind.TrimUntilEnd, 0.8, true)]
    [InlineData(SessionWindowEditKind.TrimUntilEnd, 0.95, false)]
    [InlineData(SessionWindowEditKind.Split, 0.4, false)]
    [InlineData(SessionWindowEditKind.Split, 0.5, true)]
    [InlineData(SessionWindowEditKind.Split, 0.6, false)]
    public void CanEditAt_PreservesFiveSamplesPerRemainingPart_AndRemovesAtLeastOneOnTrim(
        SessionWindowEditKind kind, double click, bool allowed) =>
        Assert.Equal(allowed, SessionWindowEditPolicy.CanEditAt(kind, click, durationSeconds: 1, sampleRate: 10));

    [Fact]
    public void Plan_TrimInsideExistingWindow_KeepsSourceTimeAndFractionalAlignment()
    {
        var sourceId = Guid.NewGuid();
        var session = TestSnapshots.Session(timestamp: 102) with
        {
            SourceSessionId = sourceId,
            WindowStartSeconds = 2.25,
            WindowEndSeconds = 8,
            GpsOffsetSeconds = 0.25
        };

        var plan = Assert.IsType<SessionWindowEditPlan.Planned>(SessionWindowEditPolicy.Plan(
            session, SessionWindowEditKind.TrimFromBeginning, 1.25));

        Assert.Equal(sourceId, plan.Current.Window!.SourceSessionId);
        Assert.Equal(3.5, plan.Current.Window.StartSeconds);
        Assert.Equal(8, plan.Current.Window.EndSeconds);
        Assert.Equal(103, plan.Current.Timestamp);
        Assert.Equal(0.5, plan.Current.GpsOffsetSeconds);
        Assert.Equal(session.DerivationWindow, plan.Current.PreviousWindow);
    }
}
