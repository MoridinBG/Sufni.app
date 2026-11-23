namespace Sufni.Telemetry.Tests;

public class TrackMotionTests
{
    private static TrackSample Fix(double time) => new(time, 7 * time + 4 * Math.Max(0, time - 30), 0, 200 - time);

    [Fact]
    public void VelocityStepIsCentredOnTheChangeInGroundSpeed()
    {
        var motion = TrackMotion.FromFixes(Enumerable.Range(0, 61).Select(i => Fix(i)), 60);
        Assert.InRange(motion.At(20)!.Value.Speed, 6.99, 7.01);
        Assert.InRange(motion.At(30)!.Value.Speed, 8.95, 9.05);
        Assert.InRange(motion.At(40)!.Value.Speed, 10.99, 11.01);
    }

    [Fact]
    public void IrregularFixesPreserveSpeedAndDistance()
    {
        RideSection[] course = [new(210, 7), new(330, 11)];
        foreach (var clock in new[] { new RideRecording(), new RideRecording(FixRate: 3, IrregularFixes: true) })
        {
            var ride = SyntheticRide.Build(course, clock);
            var motion = TrackMotion.FromFixes(ride.Fixes, ride.Telemetry.Metadata.Duration);
            Assert.InRange(motion.At(25)!.Value.Speed, 6.7, 7.3);
            Assert.InRange(motion.At(35)!.Value.Speed, 10.7, 11.3);
            Assert.InRange(motion.At(59)!.Value.Distance, 529 * 0.99, 529 * 1.01);
        }
    }

    [Fact]
    public void LateStartAndGapLimitCoverageButRetainDistanceAcrossTheLoss()
    {
        var times = Enumerable.Range(20, 6).Concat(Enumerable.Range(55, 6));
        var motion = TrackMotion.FromFixes(times.Select(t => new TrackSample(t, t * 7, 0)), 60);
        Assert.Null(motion.At(19.95));
        Assert.Equal(0, motion.At(20)!.Value.Distance);
        Assert.Null(motion.At(40));
        Assert.Equal(245, motion.At(55)!.Value.Distance, 6);
        Assert.Equal(7 * 39.45, motion.At(59.45)!.Value.Distance, 6);
    }

    [Fact]
    public void ThreeFixesCannotBridgeALossToTheNextUsableSpan()
    {
        int[] times = [0, 1, 2, 10, 15, 20, 25];
        var motion = TrackMotion.FromFixes(times.Select(t => new TrackSample(t, t * 7, 0)), 25);
        Assert.Null(motion.At(1));
        Assert.Equal(0, motion.At(10)!.Value.Distance);
        Assert.Equal(105, motion.At(25)!.Value.Distance, 6);
    }

    [Fact]
    public void DuplicateAndOutOfWindowFixesDoNotDistortTheGrid()
    {
        TrackSample[] fixes = [Fix(-1), Fix(10), Fix(11), new(11, 900, 0), Fix(13), Fix(12), Fix(14)];
        var motion = TrackMotion.FromFixes(fixes, 13);
        Assert.Null(motion.At(0));
        Assert.Null(motion.At(14));
        Assert.Equal(7, motion.At(11.5)!.Value.Speed, 6);
        Assert.Equal(188.5, motion.At(11.5)!.Value.Elevation!.Value, 6);
    }
}
