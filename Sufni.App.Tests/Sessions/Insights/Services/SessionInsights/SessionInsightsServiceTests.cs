using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Bikes.Models;
using Sufni.Telemetry;
using Sufni.App.Shared.Common;
using static Sufni.App.Tests.TestSupport.Fixtures.AnalysisTelemetryFixture;

using Sufni.App.Sessions.Insights.Services.SessionInsights;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.Sessions.Insights.Services.SessionInsights;

public class SessionInsightsServiceTests
{
    private readonly SessionInsightsService service = new();

    [Fact]
    public void ResponsePanelUsesCoveredDistanceAndComparesRatiosAgainstRepeatThresholds()
    {
        var model = new SuspensionResponseModel(Enum.GetValues<ResponseTarget>().ToImmutableDictionary(target => target,
            target => (ResponseFit)new ResponseFit.Fitted(ImuLocation.Frame, 6, 1200, [Math.Log(100), 0, 0, 0, 0])), 6, 1200);
        var profile = TerrainProfiles.Classified(Enumerable.Range(0, 81).Select(i =>
            TerrainProfiles.Window(i, i < 20 ? RoughnessClass.Calm : i < 50 ? RoughnessClass.Medium : RoughnessClass.Rough, RoughnessSource.Both)
                with { Front = new(i < 50 ? 115 : 140, 150, 105) }).ToArray());
        var input = new SessionTerrainInput(profile, new TerrainBaseline.Fitted(TerrainBaselineIdentity.Empty, model), null);
        var request = CreateRequest(CreateTelemetry(front: BuildSide(), rear: BuildSide()), terrain: input);
        var panel = service.Analyze(request).Response;
        Assert.Equal(SessionResponseState.Reported, panel.State);
        Assert.Equal(ResponseCellState.TooShort, Rms(RoughnessClass.Calm).State);
        Assert.Equal(ResponseCellState.WithinRepeatRange, Rms(RoughnessClass.Medium).State);
        Assert.Equal(ResponseCellState.Finding, Rms(RoughnessClass.Rough).State);
        Assert.Equal(1.4, Rms(RoughnessClass.Rough).Ratio!.Value, 8);
        panel = service.Analyze(request with { Selection = new TelemetryTimeSelection([new(0, 1)]),
            Terrain = input with { AnalysisRange = new(49.5, 80.5) } }).Response;
        var whole = panel.Rows.Single(r => r.Measurement.Class is null).Measurement;
        Assert.Equal(310, whole.DistanceMetres);
        Assert.Equal(Math.Exp((0.5 * Math.Log(1.15) + 30.5 * Math.Log(1.4)) / 31), Rms(null).Ratio!.Value, 8);

        ResponseCell Rms(RoughnessClass? type) => panel.Rows.Single(r => r.Measurement.Class == type)
            .Cells.Single(c => c.Measurement.Target == ResponseTarget.FrontRms).Measurement;
    }

    [Fact]
    public void Analyze_ReturnsHidden_WhenTelemetryIsMissing()
    {
        var result = service.Analyze(CreateRequest(null));

        Assert.Equal(SurfacePresentationState.Hidden, result.State);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Analyze_AttachesSupportAndReboundAdjustments_WhenSideRidesDeep()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 82, averageTravelPercent: 58, compressionBaseSpeed: 3600, reboundBaseSpeed: 1900),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionInsightsTargetProfile.Enduro));

        var finding = Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.DeepTravelUse);
        AssertAdjustment(finding, AdjustmentComponent.AirPressure, AdjustmentDirection.Add, "Fork");
        AssertAdjustment(finding, AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open, "Fork");
    }

    [Fact]
    public void Analyze_AttributesDeepSlowReboundToPacking()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 82, averageTravelPercent: 58, compressionBaseSpeed: 3600, reboundBaseSpeed: 900, reboundSlope: 5),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionInsightsTargetProfile.Enduro));

        var finding = Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.ReboundPacking);
        Assert.Equal(SessionInsightsSeverity.Action, finding.Severity);
        AssertAdjustment(finding, AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open, "Fork");
        AssertAdjustment(finding, AdjustmentComponent.LowSpeedRebound, AdjustmentDirection.Open, "Fork");
    }

    [Fact]
    public void Analyze_PrioritizesSupport_WhenDeepTravelHasRepeatedBottomouts()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 99, averageTravelPercent: 65, compressionBaseSpeed: 4200, reboundBaseSpeed: 900, bottomouts: 6),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionInsightsTargetProfile.Enduro));

        var finding = Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.SupportBeforeReboundDiagnosis);
        AssertAdjustment(finding, AdjustmentComponent.Tokens, AdjustmentDirection.Add, "Fork");
        AssertAdjustment(finding, AdjustmentComponent.AirPressure, AdjustmentDirection.Add, "Fork");
    }

    [Theory]
    [InlineData(900, 1900, 36, 5, SessionInsightsFindingId.ReboundSlowForProfileContext, AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open)]
    [InlineData(3500, 1900, 36, 22, SessionInsightsFindingId.ReboundFastForProfileContext, AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Close)]
    [InlineData(1900, 1000, 5, 22, SessionInsightsFindingId.CompressionSpeedsSubdued, AdjustmentComponent.HighSpeedCompression, AdjustmentDirection.Open)]
    [InlineData(1900, 6500, 36, 22, SessionInsightsFindingId.CompressionSpeedsHigh, AdjustmentComponent.HighSpeedCompression, AdjustmentDirection.Close)]
    public void Analyze_AttachesSideDampingAdjustments(
        double reboundBaseSpeed,
        double compressionBaseSpeed,
        double compressionSlope,
        double reboundSlope,
        SessionInsightsFindingId findingId,
        AdjustmentComponent component,
        AdjustmentDirection direction)
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(
                maxTravelPercent: 82,
                averageTravelPercent: 35,
                compressionBaseSpeed: compressionBaseSpeed,
                reboundBaseSpeed: reboundBaseSpeed,
                compressionSlope: compressionSlope,
                reboundSlope: reboundSlope),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionInsightsTargetProfile.Trail));

        AssertAdjustment(Assert.Single(result.Findings, finding => finding.Id == findingId), component, direction, "Fork");
    }

    [Fact]
    public void Analyze_ReportsLimitedBalanceContext_WhenBalancedDataIsShallowOrSlow()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 28, compressionBaseSpeed: 1200, reboundBaseSpeed: 900),
            rear: BuildSide(maxTravelPercent: 54, averageTravelPercent: 30, compressionBaseSpeed: 1250, reboundBaseSpeed: 950));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        var balanceFindings = result.Findings.Where(finding => finding.Category == SessionInsightsCategory.Balance).ToArray();
        Assert.NotEmpty(balanceFindings);
        Assert.All(balanceFindings, finding => Assert.Equal(SessionInsightsSeverity.Info, finding.Severity));
        Assert.All(balanceFindings, finding => Assert.Empty(finding.Adjustments));
    }

    [Fact]
    public void Analyze_DoesNotEmitBalanceFinding_WhenSlopesAreQuietAndDataIsRepresentative()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.DoesNotContain(result.Findings, finding => finding.Category == SessionInsightsCategory.Balance);
    }

    [Fact]
    public void Analyze_BuildsNextStep_PreferringHighestSeverityThenEarliestStep()
    {
        // Rear rides deep with chronic bottomouts: an Action shows up both as a
        // Sag travel-use finding and as a Rear packing finding. The headline
        // should take the earliest step (Sag) among the equal-severity options.
        var telemetry = CreateTelemetry(
            front: BuildSide(),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62, bottomouts: 6));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.NotNull(result.NextStep);
        var sagTitle = Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Sag).Title;
        Assert.Equal(sagTitle, result.NextStep!.Area);
        Assert.Equal(AdjustmentComponent.Tokens, result.NextStep.Adjustment.Component);
        Assert.Equal("Rear", result.NextStep.Adjustment.Side);
    }

    [Fact]
    public void Analyze_OmitsNextStep_WhenNoAdjustableFindingsExist()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Null(result.NextStep);
        Assert.False(result.HasNextStep);
    }

    [Fact]
    public void Analyze_GatesLaterStep_WhenEarlierStepHasUnresolvedIssue()
    {
        // Fork travel use is shallow (Sag issue) and the rear rebound is slow
        // (Rear issue); the Rear step should be gated behind the earlier Sag
        // issue, while Sag itself and the clean Fork step are not gated.
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 30),
            rear: BuildSide(reboundBaseSpeed: 900, reboundSlope: 5));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        var sag = Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Sag);
        var fork = Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Fork);
        var rear = Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Rear);

        Assert.True(sag.HasIssue);
        Assert.False(sag.HasGatingMessage);
        Assert.False(fork.HasGatingMessage);
        Assert.True(rear.HasIssue);
        Assert.True(rear.HasGatingMessage);
    }

    [Fact]
    public void Analyze_MarksTravelMetricStatus_RelativeToTargets()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52),
            rear: BuildSide(maxTravelPercent: 90));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        var forkMaxTravel = Assert.Single(
            Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Fork).Metrics,
            metric => metric.Label == "Max travel");
        Assert.Equal(SessionInsightsMetricStatus.BelowTarget, forkMaxTravel.Status);

        var rearMaxTravel = Assert.Single(
            Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Rear).Metrics,
            metric => metric.Label == "Max travel");
        Assert.Equal(SessionInsightsMetricStatus.Good, rearMaxTravel.Status);
    }

    private static readonly TelemetryTimeRange SelectedRange = new(0.1, 8.0);

    private static void AssertAdjustment(
        SessionInsightsFinding finding,
        AdjustmentComponent component,
        AdjustmentDirection direction,
        string side)
    {
        Assert.Contains(
            finding.Adjustments,
            adjustment => adjustment.Component == component &&
                          adjustment.Direction == direction &&
                          adjustment.Side == side);
    }
}
