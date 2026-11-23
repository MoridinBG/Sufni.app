namespace Sufni.Telemetry.Tests;

public class GpsTimeOffsetEstimatorTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(-2, 1, 0)]
    [InlineData(2, 3, 0)]
    [InlineData(2, 3, 20)]
    public void TurnsRecoverTrackDelayOnItsOriginalFixClock(double delay, double rate, double firstFix)
    {
        var ride = SyntheticRide.TurningRide(new RideRecording(FixRate: rate, IrregularFixes: rate > 1,
            FirstFix: firstFix, TrackShift: delay));
        var result = Assert.IsType<GpsTimeOffsetEstimate.Measured>(GpsTimeOffsetEstimator.Estimate(ride.Telemetry, ride.Fixes));
        Assert.InRange(result.LagSeconds, delay - 0.3, delay + 0.3);
        Assert.True(result.Correlation >= 0.65);
    }

    [Fact]
    public void StraightDescentDoesNotProduceAnOffsetSuggestion()
    {
        var ride = SyntheticRide.Build([new RideSection(700)]);
        var result = GpsTimeOffsetEstimator.Estimate(ride.Telemetry, ride.Fixes);
        Assert.False(result is GpsTimeOffsetEstimate.Measured { Correlation: >= 0.65 });
    }

    [Fact]
    public void ForkGyroCannotSupplyAFrameHeading()
    {
        var ride = SyntheticRide.TurningRide(new RideRecording(Frame: false));
        Assert.Equal(new GpsTimeOffsetEstimate.Unavailable(GpsTimeOffsetUnavailableReason.NoFrameGyro),
            GpsTimeOffsetEstimator.Estimate(ride.Telemetry, ride.Fixes));
    }
}
