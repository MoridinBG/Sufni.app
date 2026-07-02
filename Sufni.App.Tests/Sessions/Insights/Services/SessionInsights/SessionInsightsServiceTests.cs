using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using static Sufni.App.Tests.TestSupport.Fixtures.AnalysisTelemetryFixture;

using Sufni.App.Sessions.Insights.Services.SessionInsights;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.Sessions.Insights.Services.SessionInsights;

public class SessionInsightsServiceTests
{
    private readonly SessionInsightsService service = new();

    [Fact]
    public void Analyze_ReturnsHidden_WhenTelemetryIsMissing()
    {
        var result = service.Analyze(CreateRequest(null));

        Assert.Equal(SurfacePresentationState.Hidden, result.State);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Analyze_EmitsFullSessionWarning_WhenRangeIsNotSelected()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, range: null));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.DataQuality &&
            finding.Severity == SessionInsightsSeverity.Watch &&
            finding.Evidence.Any(evidence => evidence.Value == "Full session"));
    }

    [Fact]
    public void Analyze_SkipsBalanceFindings_WhenOneSuspensionEndIsMissing()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: null);

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.DoesNotContain(result.Findings, finding => finding.Category == SessionInsightsCategory.Balance);
        Assert.Contains(result.Findings, finding => finding.Category == SessionInsightsCategory.DataQuality);
    }

    [Fact]
    public void Analyze_EmitsTravelUseFindings_ForShallowTravelAndRepeatedBottomouts()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 30),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62, bottomouts: 4));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.TravelUse &&
            finding.Severity == SessionInsightsSeverity.Watch);
        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.TravelUse &&
            finding.Severity == SessionInsightsSeverity.Watch &&
            finding.Evidence.Any(evidence => evidence.Label == "Stroke bottomouts" && evidence.Value == "4"));
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.TravelUse &&
            finding.Severity == SessionInsightsSeverity.Action);

        var shallowTravel = Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.ShallowTravelUse);
        var adjustment = Assert.Single(shallowTravel.Adjustments, adjustment =>
            adjustment.Component == AdjustmentComponent.AirPressure &&
            adjustment.Direction == AdjustmentDirection.Remove);
        Assert.Equal("Fork", adjustment.Side);
        Assert.Contains("Expected:", shallowTravel.Recommendation);
    }

    [Fact]
    public void Analyze_BuildsWorkflowStepsAndDataQualityBanner()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 30),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62, bottomouts: 4));

        var result = service.Analyze(CreateRequest(telemetry, range: null));

        Assert.Equal(
            [SessionInsightsStepId.Sag, SessionInsightsStepId.Fork, SessionInsightsStepId.Rear, SessionInsightsStepId.Balance],
            result.Steps.Select(step => step.Id));
        Assert.Contains(result.DataQualityFindings, finding => finding.Id == SessionInsightsFindingId.FullSessionInsights);
        var sag = Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Sag);
        Assert.True(sag.HasIssue);
        Assert.Contains(sag.Findings, finding => finding.HasSuggestion);
        Assert.Contains(sag.Metrics, metric => metric.Label == "Fork max");
    }

    [Fact]
    public void Analyze_EscalatesBottomoutFindings_WhenBottomoutsAreChronic()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62, bottomouts: 6));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.TravelUse &&
            finding.Severity == SessionInsightsSeverity.Action &&
            finding.Evidence.Any(evidence => evidence.Label == "Stroke bottomouts" && evidence.Value == "6"));
        AssertAdjustment(
            Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.RepeatedBottomouts),
            AdjustmentComponent.Tokens,
            AdjustmentDirection.Add,
            "Rear");
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
    public void Analyze_AttributesShallowSlowCompressionToPacking_AndSuppressesDuplicateSideDamping()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 50, averageTravelPercent: 30, compressionBaseSpeed: 1000, reboundBaseSpeed: 1900),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionInsightsTargetProfile.Enduro));

        Assert.Contains(result.Findings, finding => finding.Category == SessionInsightsCategory.Packing);
        Assert.DoesNotContain(result.Findings, finding => finding.Category == SessionInsightsCategory.ForkDamping);

        var finding = Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.ResistingImpacts);
        AssertAdjustment(finding, AdjustmentComponent.HighSpeedCompression, AdjustmentDirection.Open, "Fork");
        AssertAdjustment(finding, AdjustmentComponent.AirPressure, AdjustmentDirection.Remove, "Fork");
    }

    [Fact]
    public void Analyze_AttributesDeepSlowReboundToPacking()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 82, averageTravelPercent: 58, compressionBaseSpeed: 3600, reboundBaseSpeed: 900, reboundSlope: 5),
            rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, profile: SessionInsightsTargetProfile.Enduro));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.Packing &&
            finding.Severity == SessionInsightsSeverity.Action &&
            finding.Evidence.Any(evidence => evidence.Label == "Rebound p95"));

        var finding = Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.ReboundPacking);
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

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.Packing &&
            finding.Severity == SessionInsightsSeverity.Action &&
            finding.Evidence.Any(evidence => evidence.Label == "Stroke bottomouts" && evidence.Value == "6"));

        var finding = Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.SupportBeforeReboundDiagnosis);
        AssertAdjustment(finding, AdjustmentComponent.Tokens, AdjustmentDirection.Add, "Fork");
        AssertAdjustment(finding, AdjustmentComponent.AirPressure, AdjustmentDirection.Add, "Fork");
    }

    [Theory]
    [InlineData(900, 1900, 36, 5, SessionInsightsFindingId.ReboundSlowForProfileContext, AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open)]
    [InlineData(3500, 1900, 36, 22, SessionInsightsFindingId.ReboundFastForProfileContext, AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Close)]
    [InlineData(1900, 1000, 5, 22, SessionInsightsFindingId.CompressionSpeedsSubdued, AdjustmentComponent.LowSpeedCompression, AdjustmentDirection.Open)]
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
    public void Analyze_UsesSelectedModesProfileAndRequestDampingPercentages_AsEvidence()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 80, averageTravelPercent: 35, compressionBaseSpeed: 3000, reboundBaseSpeed: 1000),
            rear: BuildSide());
        var dampingPercentages = new SessionDampingPercentages(11, 22, 33, 44, 55, 66, 77, 88);

        var result = service.Analyze(CreateRequest(
            telemetry,
            SelectedRange,
            TravelDistributionMode.DynamicSag,
            VelocityAverageMode.StrokePeakAveraged,
            BalanceDisplacementMode.Travel,
            profile: SessionInsightsTargetProfile.DH,
            dampingPercentages: dampingPercentages));

        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Dynamic sag travel stats");
        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Stroke-peak average velocity");
        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "DH profile");
        Assert.Contains(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Current damping band percentages" && evidence.Value == "11.0");
    }

    [Fact]
    public void Analyze_ShallowTravel_UsesDampingBandPercentages_ForCompressionAlternate()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 30),
            rear: BuildSide());
        var dampingPercentages = new SessionDampingPercentages(
            FrontHscPercentage: 65,
            RearHscPercentage: null,
            FrontLscPercentage: 15,
            RearLscPercentage: null,
            FrontLsrPercentage: null,
            RearLsrPercentage: null,
            FrontHsrPercentage: null,
            RearHsrPercentage: null);

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange, dampingPercentages: dampingPercentages));

        AssertAdjustment(
            Assert.Single(result.Findings, finding => finding.Id == SessionInsightsFindingId.ShallowTravelUse),
            AdjustmentComponent.HighSpeedCompression,
            AdjustmentDirection.Open,
            "Fork");
    }

    [Fact]
    public void Analyze_EmitsBalanceFindings_WhenSlopeDeltaExceedsThresholds()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(compressionSlope: 55, reboundSlope: 32),
            rear: BuildSide(compressionSlope: 28, reboundSlope: 15));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.Balance &&
            finding.Evidence.Any(evidence => evidence.Label == "Slope delta"));
    }

    [Fact]
    public void Analyze_HighSpeedBalanceMode_UsesHighSpeedBalanceAdjustment()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(compressionSlope: 55, reboundSlope: 32),
            rear: BuildSide(compressionSlope: 28, reboundSlope: 15));

        var result = service.Analyze(CreateRequest(
            telemetry,
            SelectedRange,
            balanceSpeedMode: BalanceSpeedMode.HighSpeed));

        var balance = Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Balance);
        Assert.Contains(balance.Findings, finding => finding.HasSuggestion);
        Assert.Contains(
            balance.Findings.SelectMany(finding => finding.Adjustments),
            adjustment => adjustment.Component is AdjustmentComponent.HighSpeedCompression or AdjustmentComponent.HighSpeedRebound);
    }

    [Fact]
    public void Analyze_BothSpeedBalanceMode_UsesDominantDampingBand()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(compressionSlope: 55, reboundSlope: 32),
            rear: BuildSide(compressionSlope: 28, reboundSlope: 15));
        var dampingPercentages = new SessionDampingPercentages(
            FrontHscPercentage: 10,
            RearHscPercentage: 20,
            FrontLscPercentage: 40,
            RearLscPercentage: 70,
            FrontLsrPercentage: 15,
            RearLsrPercentage: 75,
            FrontHsrPercentage: 50,
            RearHsrPercentage: 25);

        var result = service.Analyze(CreateRequest(
            telemetry,
            SelectedRange,
            balanceSpeedMode: BalanceSpeedMode.Both,
            dampingPercentages: dampingPercentages));

        Assert.Contains(
            Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Balance)
                .Findings
                .SelectMany(finding => finding.Adjustments),
            adjustment => adjustment.Component is AdjustmentComponent.LowSpeedCompression or AdjustmentComponent.LowSpeedRebound);
    }

    [Fact]
    public void Analyze_ReportsLimitedBalanceContext_WhenBalancedDataIsShallowOrSlow()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 28, compressionBaseSpeed: 1200, reboundBaseSpeed: 900),
            rear: BuildSide(maxTravelPercent: 54, averageTravelPercent: 30, compressionBaseSpeed: 1250, reboundBaseSpeed: 950));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.Balance &&
            finding.Severity == SessionInsightsSeverity.Info &&
            finding.Evidence.Any(evidence => evidence.Label == "Context limit"));
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.Balance &&
            finding.Evidence.Any(evidence => evidence.Label == "Slope delta"));
    }

    [Fact]
    public void Analyze_DoesNotEmitBalanceFinding_WhenSlopesAreQuietAndDataIsRepresentative()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.DoesNotContain(result.Findings, finding => finding.Category == SessionInsightsCategory.Balance);
    }

    [Fact]
    public void Analyze_EmitsLimitedBalanceContextAlongsideSlopeFinding_WhenDataIsTameAndSlopesDiverge()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 45, compressionSlope: 70, compressionBaseSpeed: 2500),
            rear: BuildSide(maxTravelPercent: 45, compressionSlope: 10, compressionBaseSpeed: 2500));

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.Balance &&
            finding.Evidence.Any(evidence => evidence.Label == "Slope delta"));
        Assert.Contains(result.Findings, finding =>
            finding.Category == SessionInsightsCategory.Balance &&
            finding.Severity == SessionInsightsSeverity.Info &&
            finding.Evidence.Any(evidence => evidence.Label == "Context limit"));
        Assert.DoesNotContain(
            Assert.Single(result.Steps, step => step.Id == SessionInsightsStepId.Balance).Findings,
            finding => finding.HasSuggestion);
    }

    [Fact]
    public void Analyze_DoesNotUseVibrationRecommendation_ForMixedTravelImuPairing()
    {
        var telemetry = CreateTelemetry(front: null, rear: BuildSide(), imuLocations: [(byte)ImuLocation.Fork]);

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.Contains(result.Findings, finding => finding.Category == SessionInsightsCategory.Vibration);
        Assert.DoesNotContain(result.Findings.SelectMany(finding => finding.Evidence), evidence => evidence.SourceMode == "Comparable vibration");
    }

    [Fact]
    public void Analyze_PopulatesVibrationPanel_AndKeepsVibrationFindingsOutOfSteps()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(),
            rear: BuildSide(),
            imuLocations: [(byte)ImuLocation.Fork, (byte)ImuLocation.Shock]);

        var result = service.Analyze(CreateRequest(telemetry, SelectedRange));

        Assert.NotNull(result.Vibration);
        Assert.Contains(result.Vibration!.Metrics, metric => metric.Label == "Magic carpet ratio" && metric.Side == "Fork");
        Assert.Contains(result.Vibration.Metrics, metric => metric.Label == "Magic carpet ratio" && metric.Side == "Rear");
        Assert.DoesNotContain(result.Steps.SelectMany(step => step.Findings), finding => finding.Category == SessionInsightsCategory.Vibration);
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
