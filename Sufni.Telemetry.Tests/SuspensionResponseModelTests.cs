namespace Sufni.Telemetry.Tests;

public class SuspensionResponseModelTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(1.3, true)]
    [InlineData(1, false)]
    public void HeldOutRideReportsTheResponseLawAndItsPredictor(double gain, bool lowers)
    {
        var evaluated = SyntheticResponseRides.Profile(37, gain, lowers);
        var model = SuspensionResponseModel.Fit(evaluated, SyntheticResponseRides.Baseline);
        var fit = Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.FrontRms]);
        Assert.Equal(lowers ? ImuLocation.Fork : ImuLocation.Frame, fit.Predictor);
        var cells = WholeRide(evaluated, model);
        Assert.InRange(cells[ResponseTarget.FrontRms].Ratio!.Value, gain - 0.05, gain + 0.05);
        Assert.InRange(cells[ResponseTarget.RearPeakRebound].Ratio!.Value, gain - 0.05, gain + 0.05);
        if (lowers) Assert.InRange(cells[ResponseTarget.Transmission].Ratio!.Value, 0.95, 1.05);
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
    public void FourRidesCannotFitAModelAndConstantSpeedCannotIdentifyTheSpeedEffect()
    {
        var rides = SyntheticResponseRides.Baseline;
        var few = SuspensionResponseModel.Fit(rides[5], rides[..4]);
        Assert.False(few.HasFit);
        Assert.Equal(4, few.Sessions);
        var constant = rides.Select(p => p with { Windows = [.. p.Windows.Select(w => w with { MeanSpeed = 7 })] }).ToArray();
        var model = SuspensionResponseModel.Fit(rides[5], constant[..5]);
        var failed = Assert.IsType<ResponseFit.Unavailable>(model.Fits[ResponseTarget.FrontRms]);
        Assert.True(failed.Windows >= SuspensionResponseModel.MinimumWindows);
        Assert.Equal(ResponseFitIssue.InsufficientVariation, failed.Reason);
    }

    private static Dictionary<ResponseTarget, ResponseCell> WholeRide(TerrainProfile evaluated, SuspensionResponseModel model) =>
        TerrainResponse.Describe(evaluated, model, null).Single(row => row.Class is null).Cells.ToDictionary(cell => cell.Target);
}
