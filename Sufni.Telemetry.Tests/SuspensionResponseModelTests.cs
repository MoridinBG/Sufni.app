namespace Sufni.Telemetry.Tests;

public class SuspensionResponseModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldOutRideWithTheBaselineLawRespondsAsPredicted(bool lowers)
    {
        var cells = WholeRide(SyntheticResponseRides.Profile(37, lowers: lowers));
        Assert.InRange(cells[ResponseTarget.FrontRms].Ratio!.Value, 0.95, 1.05);
        Assert.InRange(cells[ResponseTarget.RearPeakRebound].Ratio!.Value, 0.95, 1.05);
    }

    [Fact]
    public void HeldOutRideOnTheBaselineGroundTransmitsAsPredicted()
    {
        Assert.InRange(WholeRide(SyntheticResponseRides.Profile(37))[ResponseTarget.Transmission].Ratio!.Value, 0.95, 1.05);
    }

    [Fact]
    public void ThirtyPercentMoreSuspensionVelocityRaisesTheResponseByThirtyPercent()
    {
        var cells = WholeRide(SyntheticResponseRides.Profile(37, gain: 1.3));
        Assert.InRange(cells[ResponseTarget.FrontRms].Ratio!.Value, 1.25, 1.35);
        Assert.InRange(cells[ResponseTarget.RearPeakRebound].Ratio!.Value, 1.25, 1.35);
    }

    [Fact]
    public void RideWithoutLowersPredictsTheFrontFromTheFrame()
    {
        var model = SuspensionResponseModel.Fit(SyntheticResponseRides.Profile(37, lowers: false), SyntheticResponseRides.Baseline);
        Assert.Equal(ImuLocation.Frame, Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.FrontRms]).Predictor);
    }

    [Fact]
    public void IncompatibleAndAbsentLowersDoNotMakeAnUndersizedPreferredSensorSwitchToFrame()
    {
        var rides = SyntheticResponseRides.Baseline;
        var baseline = rides.Select((p, i) => i < 3 ? p : p with { LowersScale = i == 3 ? 2048 : null }).ToArray();
        var model = SuspensionResponseModel.Fit(rides[0], baseline);
        var front = Assert.IsType<ResponseFit.Unavailable>(model.Fits[ResponseTarget.FrontRms]);
        Assert.Equal(ImuLocation.Fork, front.Predictor);
        Assert.Equal(3, front.Sessions);
        Assert.Equal(ResponseFitIssue.InsufficientData, front.Reason);
        Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.RearRms]);
    }

    [Fact]
    public void FourRidesCannotFitAModel()
    {
        var rides = SyntheticResponseRides.Baseline;
        var model = SuspensionResponseModel.Fit(rides[5], rides[..4]);
        Assert.False(model.HasFit);
        Assert.Equal(4, model.Sessions);
    }

    [Fact]
    public void ConstantSpeedCannotIdentifyTheSpeedEffect()
    {
        var rides = SyntheticResponseRides.Baseline;
        var constant = rides[..5].Select(p => p with { Windows = [.. p.Windows.Select(w => w with { MeanSpeed = 7 })] }).ToArray();
        var failed = Assert.IsType<ResponseFit.Unavailable>(SuspensionResponseModel.Fit(rides[5], constant).Fits[ResponseTarget.FrontRms]);
        Assert.True(failed.Windows >= SuspensionResponseModel.MinimumWindows);
        Assert.Equal(ResponseFitIssue.InsufficientVariation, failed.Reason);
    }

    private static Dictionary<ResponseTarget, ResponseCell> WholeRide(TerrainProfile evaluated) =>
        TerrainResponse.Describe(evaluated, SuspensionResponseModel.Fit(evaluated, SyntheticResponseRides.Baseline), null)
            .Single(row => row.Class is null).Cells.ToDictionary(cell => cell.Target);
}
