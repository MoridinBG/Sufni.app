using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.AnalysisTelemetryFixture;

using Sufni.App.Sessions.Analysis.Services.SessionAnalysis;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.Services.SessionAnalysis;

public class SessionDiagnosticsTests
{
    private static readonly TelemetryTimeRange SelectedRange = new(0.1, 8.0);
    private static readonly TelemetryTimeRange LongRange = new(0.0, 30.0);

    [Fact]
    public void Run_EmitsFullSessionAnalysisFinding_WhenNoRangeIsSelected()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var report = Run(telemetry, CreateRequest(telemetry, range: null));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.FullSessionAnalysis);
        Assert.Equal(SessionAnalysisCategory.DataQuality, finding.Category);
        Assert.Equal(SessionAnalysisSeverity.Watch, finding.Severity);
        Assert.Equal(SessionAnalysisConfidence.Low, finding.Confidence);
        Assert.Equal(
            new DiagnosticEvidence(DiagnosticMeasurement.AnalysisRangeFullSession, null, null),
            Assert.Single(finding.Evidence));
        Assert.Empty(finding.Adjustments);
    }

    [Fact]
    public void Run_EmitsShortSelectedRangeFinding_WithRangeDuration()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: BuildSide());

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.ShortSelectedRange);
        var evidence = Assert.Single(finding.Evidence);
        Assert.Equal(DiagnosticMeasurement.RangeDurationSeconds, evidence.Measurement);
        Assert.Null(evidence.Side);
        Assert.Equal(SelectedRange.DurationSeconds, evidence.Value!.Value, 6);
    }

    [Fact]
    public void Run_EmitsOnlyNoStrokeStatistics_WhenBothEndsLackStrokeData()
    {
        var telemetry = CreateTelemetry(front: null, rear: null);

        var report = Run(telemetry, CreateRequest(telemetry, LongRange));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(SessionAnalysisFindingId.NoStrokeStatistics, finding.Id);
        Assert.Equal(SessionAnalysisSeverity.Watch, finding.Severity);
        Assert.Null(report.Front);
        Assert.Null(report.Rear);
    }

    [Fact]
    public void Run_EmitsOneEndMissingStrokeData_AndSkipsBalance_WhenOneEndIsMissing()
    {
        var telemetry = CreateTelemetry(front: BuildSide(), rear: null);

        var report = Run(telemetry, CreateRequest(telemetry, LongRange));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.OneEndMissingStrokeData);
        Assert.Equal(SessionAnalysisSeverity.Info, finding.Severity);
        Assert.DoesNotContain(report.Findings, finding => finding.Id == SessionAnalysisFindingId.BalanceSlopesDiverge);
        Assert.NotNull(report.Front);
        Assert.True(report.Front!.HasStrokeData);
        Assert.Equal(SuspensionType.Front, report.Front.Type);
        Assert.Equal(200, report.Front.MaxTravel);
        Assert.Null(report.Rear);
    }

    [Fact]
    public void Run_EmitsShallowTravelUse_WithTypedEvidenceAndOrderedAdjustments()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 30),
            rear: BuildSide());

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.ShallowTravelUse);
        Assert.Equal(SessionAnalysisCategory.TravelUse, finding.Category);
        Assert.Equal(SessionAnalysisSeverity.Watch, finding.Severity);
        Assert.Equal(SessionAnalysisConfidence.Medium, finding.Confidence);
        Assert.Equal(52.0, GetEvidenceValue(finding, DiagnosticMeasurement.MaxTravelPercent, SuspensionType.Front), 3);
        Assert.Equal(0.0, GetEvidenceValue(finding, DiagnosticMeasurement.Bottomouts, SuspensionType.Front), 3);
        Assert.Equal(
            SessionDiagnostics.HealthyHardSegmentTravelPercent,
            GetEvidenceValue(finding, DiagnosticMeasurement.HealthyHardSectionReference, SuspensionType.Front), 3);
        Assert.Equal(
            [
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Remove, SuspensionType.Front, 1),
                new DiagnosticAdjustment(AdjustmentComponent.LowSpeedCompression, AdjustmentDirection.Open, SuspensionType.Front, 2),
            ],
            finding.Adjustments);
    }

    [Fact]
    public void Run_SelectsHighSpeedCompressionAlternate_WhenHscBandDominates()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 52, averageTravelPercent: 30),
            rear: BuildSide());
        var damperPercentages = new SessionDamperPercentages(
            FrontHscPercentage: 65,
            RearHscPercentage: null,
            FrontLscPercentage: 15,
            RearLscPercentage: null,
            FrontLsrPercentage: null,
            RearLsrPercentage: null,
            FrontHsrPercentage: null,
            RearHsrPercentage: null);

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange, damperPercentages: damperPercentages));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.ShallowTravelUse);
        Assert.Contains(
            new DiagnosticAdjustment(AdjustmentComponent.HighSpeedCompression, AdjustmentDirection.Open, SuspensionType.Front, 2),
            finding.Adjustments);
    }

    [Theory]
    [InlineData(4, SessionAnalysisSeverity.Watch, AdjustmentComponent.AirPressure)]
    [InlineData(6, SessionAnalysisSeverity.Action, AdjustmentComponent.Tokens)]
    public void Run_GradesBottomoutSeverity_AndReordersAdjustmentPriorities(
        int bottomouts,
        SessionAnalysisSeverity expectedSeverity,
        AdjustmentComponent expectedPrimaryComponent)
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62, bottomouts: bottomouts));

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.RepeatedBottomouts);
        Assert.Equal(expectedSeverity, finding.Severity);
        Assert.Equal(bottomouts, GetEvidenceValue(finding, DiagnosticMeasurement.Bottomouts, SuspensionType.Rear), 3);
        Assert.Equal(2, finding.Adjustments.Count);
        Assert.All(finding.Adjustments, adjustment =>
        {
            Assert.Equal(SuspensionType.Rear, adjustment.Side);
            Assert.Equal(AdjustmentDirection.Add, adjustment.Direction);
        });
        Assert.Equal(
            expectedPrimaryComponent,
            Assert.Single(finding.Adjustments, adjustment => adjustment.Priority == 1).Component);
    }

    [Fact]
    public void Run_EmitsDynamicSagMismatch_TowardTheDeeperEnd()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(averageTravelPercent: 30),
            rear: BuildSide(maxTravelPercent: 98, averageTravelPercent: 62));

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.DynamicSagMismatch);
        Assert.Equal(SessionAnalysisCategory.Balance, finding.Category);
        Assert.Equal(SessionAnalysisSeverity.Watch, finding.Severity);
        Assert.Equal(30.0, GetEvidenceValue(finding, DiagnosticMeasurement.AverageTravelPercent, SuspensionType.Front), 3);
        Assert.Equal(62.0, GetEvidenceValue(finding, DiagnosticMeasurement.AverageTravelPercent, SuspensionType.Rear), 3);
        Assert.Equal(
            [
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Add, SuspensionType.Rear, 1),
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Remove, SuspensionType.Front, 2),
            ],
            finding.Adjustments);
    }

    [Fact]
    public void Run_AttributesShallowSlowCompressionToPacking_AndSuppressesSideDamping()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 50, averageTravelPercent: 30, compressionBaseSpeed: 1000, reboundBaseSpeed: 1900),
            rear: BuildSide());

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange, profile: SessionAnalysisTargetProfile.Enduro));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.ResistingImpacts);
        Assert.Equal(SessionAnalysisCategory.Packing, finding.Category);
        Assert.Equal(SessionAnalysisSeverity.Watch, finding.Severity);
        Assert.Contains(finding.Evidence, evidence =>
            evidence.Measurement == DiagnosticMeasurement.CompressionP95 &&
            evidence.Side == SuspensionType.Front &&
            evidence.Value is not null);
        Assert.Contains(
            new DiagnosticEvidence(DiagnosticMeasurement.CompressionReferenceBand, SuspensionType.Front, null),
            finding.Evidence);
        Assert.Contains(finding.Evidence, evidence => evidence.Measurement == DiagnosticMeasurement.DamperBandHsc);
        Assert.Equal(
            [
                new DiagnosticAdjustment(AdjustmentComponent.HighSpeedCompression, AdjustmentDirection.Open, SuspensionType.Front, 1),
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Remove, SuspensionType.Front, 2),
            ],
            finding.Adjustments);
        Assert.DoesNotContain(report.Findings, finding => finding.Category == SessionAnalysisCategory.ForkDamping);
    }

    [Fact]
    public void Run_FlagsSlowRebound_AgainstProfileReference()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(reboundBaseSpeed: 900, reboundSlope: 5),
            rear: BuildSide());

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange, profile: SessionAnalysisTargetProfile.Trail));

        var finding = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.ReboundSlowForProfileContext);
        Assert.Equal(SessionAnalysisCategory.ForkDamping, finding.Category);
        Assert.Equal(SessionAnalysisSeverity.Watch, finding.Severity);
        var reboundP95 = GetEvidenceValue(finding, DiagnosticMeasurement.ReboundP95, SuspensionType.Front);
        Assert.InRange(reboundP95, 1.0, SessionDiagnostics.GetProfileReferences(SessionAnalysisTargetProfile.Trail).Rebound.Low);
        Assert.Equal(
            [new DiagnosticAdjustment(AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open, SuspensionType.Front, 1)],
            finding.Adjustments);
    }

    [Fact]
    public void Run_BalanceAdjustmentClosesDominantCompressionBand_WhenSpeedsAreHealthy()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(compressionSlope: 55, reboundSlope: 32),
            rear: BuildSide(compressionSlope: 28, reboundSlope: 15));

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var finding = Assert.Single(report.Findings, finding =>
            finding.Id == SessionAnalysisFindingId.BalanceSlopesDiverge &&
            finding.Evidence.Any(evidence => evidence.Measurement == DiagnosticMeasurement.CompressionBalanceSlopeDeltaPercent));
        Assert.Equal(SessionAnalysisCategory.Balance, finding.Category);
        Assert.Contains(finding.Evidence, evidence =>
            evidence.Measurement == DiagnosticMeasurement.FrontBalanceSlope && evidence.Side == SuspensionType.Front);
        Assert.Contains(finding.Evidence, evidence =>
            evidence.Measurement == DiagnosticMeasurement.RearBalanceSlope && evidence.Side == SuspensionType.Rear);
        var adjustment = Assert.Single(finding.Adjustments);
        Assert.Equal(AdjustmentComponent.LowSpeedCompression, adjustment.Component);
        Assert.Equal(AdjustmentDirection.Close, adjustment.Direction);
        Assert.Equal(1, adjustment.Priority);
    }

    [Fact]
    public void Run_LimitedBalanceContext_SuppressesBalanceAdjustments()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(maxTravelPercent: 45, compressionSlope: 70, compressionBaseSpeed: 2500),
            rear: BuildSide(maxTravelPercent: 45, compressionSlope: 10, compressionBaseSpeed: 2500));

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var diverging = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.BalanceSlopesDiverge);
        Assert.Empty(diverging.Adjustments);
        var context = Assert.Single(report.Findings, finding => finding.Id == SessionAnalysisFindingId.BalanceContextLimited);
        Assert.Equal(SessionAnalysisSeverity.Info, context.Severity);
        Assert.Contains(
            new DiagnosticEvidence(DiagnosticMeasurement.BalanceContextLimit, null, null),
            context.Evidence);
        Assert.Contains(context.Evidence, evidence =>
            evidence.Measurement == DiagnosticMeasurement.MaxTravelPercent && evidence.Side == SuspensionType.Front);
        Assert.Contains(context.Evidence, evidence =>
            evidence.Measurement == DiagnosticMeasurement.MaxTravelPercent && evidence.Side == SuspensionType.Rear);
    }

    [Fact]
    public void Run_EmitsVibrationContextPerInstrumentedSide()
    {
        var telemetry = CreateTelemetry(
            front: BuildSide(),
            rear: BuildSide(),
            imuLocations: [(byte)ImuLocation.Fork, (byte)ImuLocation.Shock]);

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var vibrationFindings = report.Findings
            .Where(finding => finding.Id == SessionAnalysisFindingId.VibrationContext)
            .ToList();
        Assert.Equal(2, vibrationFindings.Count);
        Assert.All(vibrationFindings, finding =>
        {
            Assert.Equal(SessionAnalysisCategory.Vibration, finding.Category);
            Assert.Equal(SessionAnalysisSeverity.Info, finding.Severity);
            Assert.Empty(finding.Adjustments);
            Assert.Equal(
                [
                    DiagnosticMeasurement.MagicCarpetRatio,
                    DiagnosticMeasurement.AverageG,
                    DiagnosticMeasurement.CompressionVibrationPercent,
                    DiagnosticMeasurement.ReboundVibrationPercent,
                ],
                finding.Evidence.Select(evidence => evidence.Measurement));
        });
        Assert.Contains(vibrationFindings, finding => finding.Evidence.All(evidence => evidence.Side == SuspensionType.Front));
        Assert.Contains(vibrationFindings, finding => finding.Evidence.All(evidence => evidence.Side == SuspensionType.Rear));
    }

    [Fact]
    public void Run_FallsBackToVibrationNotice_WhenImuPairingIsNotComparable()
    {
        var telemetry = CreateTelemetry(front: null, rear: BuildSide(), imuLocations: [(byte)ImuLocation.Fork]);

        var report = Run(telemetry, CreateRequest(telemetry, SelectedRange));

        var finding = Assert.Single(report.Findings, finding => finding.Category == SessionAnalysisCategory.Vibration);
        Assert.Equal(SessionAnalysisFindingId.VibrationNotUsedForRecommendations, finding.Id);
        Assert.Equal(SessionAnalysisSeverity.Info, finding.Severity);
        Assert.Empty(finding.Evidence);
    }

    private static double GetEvidenceValue(
        DiagnosticFinding finding,
        DiagnosticMeasurement measurement,
        SuspensionType side)
    {
        var evidence = Assert.Single(finding.Evidence, evidence =>
            evidence.Measurement == measurement && evidence.Side == side);
        Assert.NotNull(evidence.Value);
        return evidence.Value!.Value;
    }

    private static SessionDiagnosticsReport Run(TelemetryData telemetry, SessionAnalysisRequest request)
    {
        var travelOptions = new TravelStatisticsOptions(request.AnalysisRange, request.TravelHistogramMode);
        var velocityOptions = new VelocityStatisticsOptions(request.AnalysisRange, request.VelocityAverageMode);
        var balanceOptions = new BalanceStatisticsOptions(
            request.AnalysisRange,
            request.BalanceDisplacementMode,
            request.BalanceSpeedMode,
            request.DampingSpeedCutoffs.Front.CompressionMmPerSecond,
            request.DampingSpeedCutoffs.Front.ReboundMmPerSecond,
            request.DampingSpeedCutoffs.Rear.CompressionMmPerSecond,
            request.DampingSpeedCutoffs.Rear.ReboundMmPerSecond);
        var context = new AnalysisContext(
            request,
            travelOptions,
            velocityOptions,
            balanceOptions,
            SessionDiagnostics.GetProfileReferences(request.TargetProfile));

        return SessionDiagnostics.Run(telemetry, context);
    }
}
