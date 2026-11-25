namespace Sufni.Telemetry.Tests;

public class SuspensionResponseModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldOutRideWithTheBaselineLawRespondsAsPredicted(bool fork)
    {
        var cells = WholeRide(SyntheticResponseRides.Profile(37, fork: fork));
        Assert.InRange(cells[ResponseTarget.FrontRms].Ratio!.Value, 0.95, 1.05);
        Assert.InRange(cells[ResponseTarget.RearPeakRebound].Ratio!.Value, 0.95, 1.05);
    }

    [Fact]
    public void HeldOutRideOnTheBaselineGroundTransmitsAsPredicted()
    {
        Assert.InRange(WholeRide(SyntheticResponseRides.Profile(37))[ResponseTarget.PassedToFrame].Ratio!.Value, 0.95, 1.05);
    }

    [Fact]
    public void ThirtyPercentMoreSuspensionVelocityRaisesTheResponseByThirtyPercent()
    {
        var cells = WholeRide(SyntheticResponseRides.Profile(37, gain: 1.3));
        Assert.InRange(cells[ResponseTarget.FrontRms].Ratio!.Value, 1.25, 1.35);
        Assert.InRange(cells[ResponseTarget.RearPeakRebound].Ratio!.Value, 1.25, 1.35);
    }

    [Fact]
    public void ResponseBeyondTheRepeatRangeIsAFindingWithItsSignedDeviation()
    {
        var evaluated = SyntheticResponseRides.Profile(37, gain: 0.6);
        var rows = TerrainResponse.Describe(evaluated, SuspensionResponseModel.Fit(evaluated, SyntheticResponseRides.Baseline), null);
        var findings = TerrainResponse.Findings(rows).Where(f => f.Group is null).ToDictionary(f => f.Target);
        Assert.InRange(findings[ResponseTarget.RearPeakRebound].Deviation, -0.45, -0.35);
        Assert.Equal(ResponseConfidence.Full, findings[ResponseTarget.FrontRms].Confidence);
        Assert.DoesNotContain(ResponseTarget.PassedToFrame, findings.Keys);
        Assert.Empty(TerrainResponse.Findings(TerrainResponse.Describe(SyntheticResponseRides.Profile(37),
            SuspensionResponseModel.Fit(SyntheticResponseRides.Profile(37), SyntheticResponseRides.Baseline), null)));
    }

    [Theory]
    [InlineData(true, true, GroundSensors.Fork, GroundSensors.RearAxle, GroundSensors.ForkAndRearAxle)]
    [InlineData(true, false, GroundSensors.Fork, GroundSensors.Frame, GroundSensors.Fork)]
    [InlineData(false, true, GroundSensors.Frame, GroundSensors.RearAxle, GroundSensors.RearAxle)]
    [InlineData(false, false, GroundSensors.Frame, GroundSensors.Frame, null)]
    public void EachTargetReadsTheGroundFromTheFirstSetTheRideCarries(bool fork, bool rearAxle, GroundSensors front,
        GroundSensors rear, GroundSensors? passedToFrame)
    {
        var model = SuspensionResponseModel.Fit(SyntheticResponseRides.Profile(37, fork: fork, rearAxle: rearAxle),
            SyntheticResponseRides.Baseline);
        Assert.Equal(front, Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.FrontRms]).Predictors);
        Assert.Equal(rear, Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.RearRms]).Predictors);
        if (passedToFrame is { } sensors)
            Assert.Equal(sensors, Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.PassedToFrame]).Predictors);
        else
            Assert.Equal(ResponseFitIssue.MissingSensor,
                Assert.IsType<ResponseFit.Unavailable>(model.Fits[ResponseTarget.PassedToFrame]).Reason);
    }

    [Fact]
    public void SetWithTooFewBaselineRidesPassesToTheNextSet()
    {
        var baseline = SyntheticResponseRides.Baseline
            .Select((p, i) => i < 3 ? p : p with { RearAxleScale = i == 3 ? 2048 : null }).ToArray();
        var model = SuspensionResponseModel.Fit(SyntheticResponseRides.Profile(37), baseline);
        Assert.Equal(GroundSensors.Frame, Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.RearRms]).Predictors);
        Assert.Equal(GroundSensors.Fork, Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.PassedToFrame]).Predictors);
    }

    [Fact]
    public void ShockWithoutRearAxleOrFrameHasNoForkFallback()
    {
        var model = SuspensionResponseModel.Fit(SyntheticResponseRides.Profile(37, frame: false, rearAxle: false),
            SyntheticResponseRides.Baseline);
        var rear = Assert.IsType<ResponseFit.Unavailable>(model.Fits[ResponseTarget.RearRms]);
        Assert.Equal(ResponseFitIssue.MissingSensor, rear.Reason);
        Assert.IsType<ResponseFit.Fitted>(model.Fits[ResponseTarget.FrontRms]);
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

    [Fact]
    public void ClimbingBaselineRidesDoNotFitTheGroundModel()
    {
        var climbing = SyntheticResponseRides.Baseline.Select(Climbing).ToArray();
        var model = SuspensionResponseModel.Fit(SyntheticResponseRides.Profile(37), climbing);
        Assert.False(model.HasFit);
        Assert.Equal(0, model.Windows);
    }

    [Fact]
    public void ClimbingWindowsStayOutOfEveryResponseRow()
    {
        var profile = SyntheticResponseRides.Profile(37);
        var evaluated = profile with
        {
            Windows = [.. profile.Windows.Select(w => w.StartMetres < 1100
                ? w with { Band = GradeBand.Climb, Roughness = RoughnessClass.TechnicalClimb, Source = null } : w)]
        };
        var rows = TerrainResponse.Describe(evaluated, SuspensionResponseModel.Fit(evaluated, SyntheticResponseRides.Baseline), null);
        Assert.Equal(10 * profile.Windows.Count(w => w.Moving && w.StartMetres >= 1100), rows.Single(row => row.Class is null).DistanceMetres, 6);
        Assert.DoesNotContain(rows, row => row.Class?.IsClimb() == true);
    }

    [Fact]
    public void AFilterLimitsEveryResponseRowToTheWindowsItAdmits()
    {
        var evaluated = SyntheticResponseRides.Profile(37);
        var rows = TerrainResponse.Describe(evaluated, SuspensionResponseModel.Fit(evaluated, SyntheticResponseRides.Baseline), null,
            w => w.StartMetres >= 1100);
        Assert.Equal(10 * evaluated.Windows.Count(w => w.Moving && w.StartMetres >= 1100), rows.Single(row => row.Class is null).DistanceMetres, 6);
    }

    private static TerrainProfile Climbing(TerrainProfile profile) =>
        profile with { Windows = [.. profile.Windows.Select(w => w with { Band = GradeBand.Climb })] };

    private static Dictionary<ResponseTarget, ResponseCell> WholeRide(TerrainProfile evaluated) =>
        TerrainResponse.Describe(evaluated, SuspensionResponseModel.Fit(evaluated, SyntheticResponseRides.Baseline), null)
            .Single(row => row.Class is null).Cells.ToDictionary(cell => cell.Target);
}
