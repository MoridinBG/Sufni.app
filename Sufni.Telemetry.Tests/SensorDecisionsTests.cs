namespace Sufni.Telemetry.Tests;

public class SensorDecisionsTests
{
    private static readonly SensorIdentity Shock = new(SensorKind.ShockTravel);
    private static readonly SensorIdentity Fork = new(SensorKind.ForkTravel);

    private static SensorFinding Finding(SensorIdentity sensor, SensorFaultReason reason, params (double Start, double End)[] ranges) =>
        new(sensor, reason, [.. ranges.Select(range => new SourceTimeRange(range.Start, range.End))]);

    [Fact]
    public void Undecided_ReturnsEveryFinding_WhenNothingIsDecided()
    {
        var findings = new[] { Finding(Shock, SensorFaultReason.OutOfRange, (10, 50)), Finding(Fork, SensorFaultReason.Frozen, (5, 9)) };

        Assert.Equal(findings, SensorDecisions.None.Undecided(findings));
    }

    [Fact]
    public void Undecided_LeavesOutAFinding_ThatDecisionsCoverFully()
    {
        var decisions = SensorDecisions.None.With(Shock, SensorDecision.Keep, [new(0, 60)]);

        Assert.Empty(decisions.Undecided([Finding(Shock, SensorFaultReason.OutOfRange, (10, 50))]));
    }

    [Fact]
    public void Undecided_ReturnsTheUncoveredPart_OfAPartlyDecidedRange()
    {
        var decisions = SensorDecisions.None.With(Shock, SensorDecision.Drop, [new(10, 30)]);

        var finding = Assert.Single(decisions.Undecided([Finding(Shock, SensorFaultReason.OutOfRange, (10, 50))]));

        Assert.Equal([new SourceTimeRange(30, 50)], finding.Ranges);
    }

    [Fact]
    public void Undecided_LeavesOutAShortRemainder_ButKeepsAShortUncutRange()
    {
        var decisions = SensorDecisions.None.With(Shock, SensorDecision.Keep, [new(10, 49)]);

        var finding = Assert.Single(decisions.Undecided([Finding(Shock, SensorFaultReason.Glitch, (10, 50), (70, 70.5))]));

        Assert.Equal([new SourceTimeRange(70, 70.5)], finding.Ranges);
    }

    [Fact]
    public void Undecided_UsesOnlyTheDecisionsOfTheFindingsSensor()
    {
        var decisions = SensorDecisions.None.With(Fork, SensorDecision.Keep, [new(0, 60)]);

        Assert.Single(decisions.Undecided([Finding(Shock, SensorFaultReason.OutOfRange, (10, 50))]));
    }

    [Fact]
    public void Undecided_PassesNoticesUnchanged()
    {
        var gps = new SensorIdentity(SensorKind.Gps);
        var decisions = SensorDecisions.None.With(gps, SensorDecision.Keep, [new(0, 60)]);
        var notice = Finding(gps, SensorFaultReason.FixLost, (10, 50));

        Assert.Equal([notice], decisions.Undecided([notice]));
    }

    [Fact]
    public void With_JoinsTouchingRangesOfOneDecision()
    {
        var decisions = SensorDecisions.None
            .With(Shock, SensorDecision.Drop, [new(20, 30)])
            .With(Shock, SensorDecision.Drop, [new(10, 20)]);

        Assert.Equal([new SourceTimeRange(10, 30)], decisions.DropRanges(Shock));
    }

    [Fact]
    public void With_ReplacesAnEarlierDecision_WhereTheyOverlap()
    {
        var decisions = SensorDecisions.None
            .With(Shock, SensorDecision.Keep, [new(10, 30)])
            .With(Shock, SensorDecision.Drop, [new(20, 40)]);

        Assert.Equal([new SourceTimeRange(20, 40)], decisions.DropRanges(Shock));
    }

    [Fact]
    public void With_KeepsTheDecisionsOfOtherSensors()
    {
        var decisions = SensorDecisions.None
            .With(Fork, SensorDecision.Drop, [new(1, 5)])
            .With(Shock, SensorDecision.Keep, [new(10, 30)]);

        Assert.Equal([new SourceTimeRange(1, 5)], decisions.DropRanges(Fork));
    }

    [Fact]
    public void Drops_AreEqual_WhenOnlyKeepDecisionsDiffer()
    {
        var dropped = SensorDecisions.None.With(Shock, SensorDecision.Drop, [new(10, 30)]);

        Assert.Equal(dropped.Drops, dropped.With(Fork, SensorDecision.Keep, [new(1, 5)]).Drops);
        Assert.NotEqual(dropped.Drops, dropped.With(Fork, SensorDecision.Drop, [new(1, 5)]).Drops);
    }
}
