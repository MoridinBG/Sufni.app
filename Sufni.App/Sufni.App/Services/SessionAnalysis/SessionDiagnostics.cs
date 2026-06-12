using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Models;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;

namespace Sufni.App.Services.SessionAnalysis;

/// <summary>
/// The analysis heuristics. Emits typed findings only — no user-facing text
/// is produced here; the presenter and text catalog own every string.
/// </summary>
internal static class SessionDiagnostics
{
    public const double ShortRangeSeconds = 15.0;
    public const int MinimumUsefulStrokeCount = 8;
    public const double ShallowMaxTravelPercent = 60.0;
    public const double HealthyHardSegmentTravelPercent = 85.0;
    public const double DeepAverageTravelPercent = 45.0;
    public const double HighAverageTravelPercent = 55.0;
    public const double DynamicSagMismatchWatchPercent = 15.0;
    public const int RepeatedBottomoutCount = 3;
    public const int ChronicBottomoutCount = 6;
    public const double CompressionBalanceSlopeWatchPercent = 10.0;
    public const double ReboundBalanceSlopeWatchPercent = 20.0;
    public const double VeryFastMultiplier = 1.25;

    public static SessionDiagnosticsReport Run(TelemetryData telemetryData, AnalysisContext context)
    {
        var findings = new List<DiagnosticFinding>();

        AddDataQualityFindings(telemetryData, context, findings);

        var front = CreateSideSnapshot(telemetryData, SuspensionType.Front, context);
        var rear = CreateSideSnapshot(telemetryData, SuspensionType.Rear, context);

        AddTravelUseFindings(front, context, findings);
        AddTravelUseFindings(rear, context, findings);
        AddDynamicSagBalanceFinding(front, rear, context, findings);

        var packingSides = new HashSet<SuspensionType>();
        AddPackingFindings(front, context, findings, packingSides);
        AddPackingFindings(rear, context, findings, packingSides);

        AddSideDampingFindings(front, context, findings, packingSides);
        AddSideDampingFindings(rear, context, findings, packingSides);

        AddBalanceFindings(telemetryData, front, rear, context, findings);
        AddVibrationFindings(telemetryData, front, rear, context, findings);

        return new SessionDiagnosticsReport(front, rear, findings);
    }

    public static ProfileReferences GetProfileReferences(SessionAnalysisTargetProfile profile)
    {
        return profile switch
        {
            SessionAnalysisTargetProfile.Weekend => new ProfileReferences(new SpeedBand(1200, 1800), new SpeedBand(1600, 3000)),
            SessionAnalysisTargetProfile.Trail => new ProfileReferences(new SpeedBand(1500, 2200), new SpeedBand(2200, 4000)),
            SessionAnalysisTargetProfile.Enduro => new ProfileReferences(new SpeedBand(1800, 2500), new SpeedBand(3000, 5500)),
            SessionAnalysisTargetProfile.DH => new ProfileReferences(new SpeedBand(2200, 3000), new SpeedBand(4500, 7000)),
            _ => new ProfileReferences(new SpeedBand(1500, 2200), new SpeedBand(2200, 4000)),
        };
    }

    private static void AddDataQualityFindings(
        TelemetryData telemetryData,
        AnalysisContext context,
        List<DiagnosticFinding> findings)
    {
        if (context.Request.AnalysisRange is null)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.FullSessionAnalysis,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Low,
                [new DiagnosticEvidence(DiagnosticMeasurement.AnalysisRangeFullSession, null, null)],
                []));
        }
        else if (context.Request.AnalysisRange.Value.DurationSeconds < ShortRangeSeconds)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.ShortSelectedRange,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Low,
                [new DiagnosticEvidence(DiagnosticMeasurement.RangeDurationSeconds, null, context.Request.AnalysisRange.Value.DurationSeconds)],
                []));
        }

        var frontHasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Front, context.Request.AnalysisRange);
        var rearHasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Rear, context.Request.AnalysisRange);
        if (!frontHasStrokeData && !rearHasStrokeData)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.NoStrokeStatistics,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Low,
                [],
                []));
            return;
        }

        if (frontHasStrokeData != rearHasStrokeData)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.OneEndMissingStrokeData,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                [],
                []));
        }

        if (!TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Compression, context.BalanceOptions) &&
            !TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Rebound, context.BalanceOptions))
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.BalanceUnavailable,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                [new DiagnosticEvidence(DiagnosticMeasurement.BalanceMode, null, null)],
                []));
        }
    }

    private static SideSnapshot? CreateSideSnapshot(
        TelemetryData telemetryData,
        SuspensionType side,
        AnalysisContext context)
    {
        var suspension = GetSuspension(telemetryData, side);
        if (!suspension.Present)
        {
            return null;
        }

        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, side, context.Request.AnalysisRange);
        var travelStatistics = TelemetryStatistics.CalculateTravelStatistics(telemetryData, side, context.TravelOptions);
        var velocityStatistics = TelemetryStatistics.CalculateVelocityStatistics(telemetryData, side, context.VelocityOptions);
        var maxTravel = suspension.MaxTravel;
        var maxPercent = ToPercent(travelStatistics.Max, maxTravel);
        var averagePercent = ToPercent(travelStatistics.Average, maxTravel);
        var strokeCount = velocityStatistics.CompressionStrokeCount + velocityStatistics.ReboundStrokeCount;

        return new SideSnapshot(
            side,
            hasStrokeData,
            travelStatistics,
            velocityStatistics,
            maxTravel,
            maxPercent,
            averagePercent,
            strokeCount);
    }

    private static void AddTravelUseFindings(
        SideSnapshot? side,
        AnalysisContext context,
        List<DiagnosticFinding> findings)
    {
        if (side is null || !side.HasStrokeData)
        {
            return;
        }

        if (side.StrokeCount < MinimumUsefulStrokeCount)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.LowStrokeCount,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                [new DiagnosticEvidence(DiagnosticMeasurement.StrokeCount, side.Type, side.StrokeCount)],
                []));
        }

        if (side.MaxTravelPercent is not null && side.MaxTravelPercent < ShallowMaxTravelPercent)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.ShallowTravelUse,
                SessionAnalysisCategory.TravelUse,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                CreateTravelEvidence(side),
                [
                    new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Remove, side.Type, 1),
                    new DiagnosticAdjustment(SelectCompressionOpenComponent(side, context), AdjustmentDirection.Open, side.Type, 2),
                ]));
        }

        var bottomoutSeverity = GetBottomoutSeverity(side);
        if (bottomoutSeverity is not null)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.RepeatedBottomouts,
                SessionAnalysisCategory.TravelUse,
                bottomoutSeverity.Value,
                CreateConfidence(context, side),
                CreateTravelEvidence(side),
                CreateBottomoutAdjustments(side, bottomoutSeverity.Value)));
        }

        if (side.AverageTravelPercent is not null &&
            side.AverageTravelPercent > HighAverageTravelPercent &&
            bottomoutSeverity is null)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.DeepTravelUse,
                SessionAnalysisCategory.TravelUse,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                CreateTravelEvidence(side),
                [
                    new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Add, side.Type, 1),
                    new DiagnosticAdjustment(AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open, side.Type, 2),
                ]));
        }
    }

    private static IReadOnlyList<DiagnosticAdjustment> CreateBottomoutAdjustments(
        SideSnapshot side,
        SessionAnalysisSeverity bottomoutSeverity)
    {
        return bottomoutSeverity == SessionAnalysisSeverity.Action
            ?
            [
                new DiagnosticAdjustment(AdjustmentComponent.Tokens, AdjustmentDirection.Add, side.Type, 1),
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Add, side.Type, 2),
            ]
            :
            [
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Add, side.Type, 1),
                new DiagnosticAdjustment(AdjustmentComponent.Tokens, AdjustmentDirection.Add, side.Type, 2),
            ];
    }

    private static void AddDynamicSagBalanceFinding(
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        List<DiagnosticFinding> findings)
    {
        if (front?.AverageTravelPercent is not { } frontAverage || rear?.AverageTravelPercent is not { } rearAverage)
        {
            return;
        }

        var delta = frontAverage - rearAverage;
        if (Math.Abs(delta) < DynamicSagMismatchWatchPercent)
        {
            return;
        }

        var deeperSide = delta > 0 ? front : rear;
        var shallowerSide = delta > 0 ? rear : front;
        findings.Add(new DiagnosticFinding(
            SessionAnalysisFindingId.DynamicSagMismatch,
            SessionAnalysisCategory.Balance,
            SessionAnalysisSeverity.Watch,
            CreateConfidence(context, front, rear),
            [
                new DiagnosticEvidence(DiagnosticMeasurement.AverageTravelPercent, front.Type, frontAverage),
                new DiagnosticEvidence(DiagnosticMeasurement.AverageTravelPercent, rear.Type, rearAverage),
            ],
            [
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Add, deeperSide.Type, 1),
                new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Remove, shallowerSide.Type, 2),
            ]));
    }

    private static void AddPackingFindings(
        SideSnapshot? side,
        AnalysisContext context,
        List<DiagnosticFinding> findings,
        HashSet<SuspensionType> packingSides)
    {
        if (side is null || !side.HasStrokeData)
        {
            return;
        }

        var reboundP95 = Math.Abs(side.Velocity.Percentile95Rebound);
        var compressionP95 = Math.Abs(side.Velocity.Percentile95Compression);
        var lowRebound = IsBelowReference(reboundP95, context.Profile.Rebound);
        var lowCompression = IsBelowReference(compressionP95, context.Profile.Compression);
        var shallowTravel = side.MaxTravelPercent is not null && side.MaxTravelPercent < ShallowMaxTravelPercent;
        var deepAverage = side.AverageTravelPercent is not null && side.AverageTravelPercent > DeepAverageTravelPercent;
        var bottomoutSeverity = GetBottomoutSeverity(side);
        var repeatedBottomouts = bottomoutSeverity is not null;

        if (deepAverage && lowRebound && !repeatedBottomouts)
        {
            packingSides.Add(side.Type);
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.ReboundPacking,
                SessionAnalysisCategory.Packing,
                SessionAnalysisSeverity.Action,
                CreateConfidence(context, side),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                [
                    new DiagnosticAdjustment(AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open, side.Type, 1),
                    new DiagnosticAdjustment(AdjustmentComponent.LowSpeedRebound, AdjustmentDirection.Open, side.Type, 2),
                ]));
            return;
        }

        if (deepAverage && repeatedBottomouts)
        {
            packingSides.Add(side.Type);
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.SupportBeforeReboundDiagnosis,
                SessionAnalysisCategory.Packing,
                bottomoutSeverity!.Value,
                CreateConfidence(context, side),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                CreateBottomoutAdjustments(side, bottomoutSeverity.Value)));
            return;
        }

        if (shallowTravel && lowCompression)
        {
            packingSides.Add(side.Type);
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.ResistingImpacts,
                SessionAnalysisCategory.Packing,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                [
                    new DiagnosticAdjustment(AdjustmentComponent.HighSpeedCompression, AdjustmentDirection.Open, side.Type, 1),
                    new DiagnosticAdjustment(AdjustmentComponent.AirPressure, AdjustmentDirection.Remove, side.Type, 2),
                ]));
        }
    }

    private static void AddSideDampingFindings(
        SideSnapshot? side,
        AnalysisContext context,
        List<DiagnosticFinding> findings,
        HashSet<SuspensionType> packingSides)
    {
        if (side is null || !side.HasStrokeData || packingSides.Contains(side.Type))
        {
            return;
        }

        var category = side.Type == SuspensionType.Front
            ? SessionAnalysisCategory.ForkDamping
            : SessionAnalysisCategory.RearDamping;
        var reboundP95 = Math.Abs(side.Velocity.Percentile95Rebound);
        var compressionP95 = Math.Abs(side.Velocity.Percentile95Compression);

        if (IsBelowReference(reboundP95, context.Profile.Rebound))
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.ReboundSlowForProfileContext,
                category,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                CreateDampingEvidence(side, context, includeTravel: false, includeDamperBands: true),
                [new DiagnosticAdjustment(AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Open, side.Type, 1)]));
        }
        else if (IsAboveFastReference(reboundP95, context.Profile.Rebound))
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.ReboundFastForProfileContext,
                category,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                CreateDampingEvidence(side, context, includeTravel: false, includeDamperBands: true),
                [new DiagnosticAdjustment(AdjustmentComponent.HighSpeedRebound, AdjustmentDirection.Close, side.Type, 1)]));
        }

        if (side.Travel.Bottomouts >= RepeatedBottomoutCount)
        {
            return;
        }

        if (IsBelowReference(compressionP95, context.Profile.Compression))
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.CompressionSpeedsSubdued,
                category,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, side),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                [new DiagnosticAdjustment(SelectCompressionOpenComponent(side, context), AdjustmentDirection.Open, side.Type, 1)]));
        }
        else if (IsAboveFastReference(compressionP95, context.Profile.Compression))
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.CompressionSpeedsHigh,
                category,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, side),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                [new DiagnosticAdjustment(AdjustmentComponent.HighSpeedCompression, AdjustmentDirection.Close, side.Type, 1)]));
        }
    }

    private static void AddBalanceFindings(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        List<DiagnosticFinding> findings)
    {
        if (front is null || rear is null)
        {
            return;
        }

        var hasLimitedBalanceContext = HasLimitedBalanceContext(front, rear, context);
        AddBalanceFinding(telemetryData, BalanceType.Rebound, ReboundBalanceSlopeWatchPercent, front, rear, hasLimitedBalanceContext, context, findings);
        AddBalanceFinding(telemetryData, BalanceType.Compression, CompressionBalanceSlopeWatchPercent, front, rear, hasLimitedBalanceContext, context, findings);
        if (HasAnyBalanceData(telemetryData, context) && hasLimitedBalanceContext)
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.BalanceContextLimited,
                SessionAnalysisCategory.Balance,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, front, rear),
                CreateBalanceContextEvidence(front, rear),
                []));
        }
    }

    private static void AddBalanceFinding(
        TelemetryData telemetryData,
        BalanceType balanceType,
        double thresholdPercent,
        SideSnapshot front,
        SideSnapshot rear,
        bool hasLimitedBalanceContext,
        AnalysisContext context,
        List<DiagnosticFinding> findings)
    {
        if (!TelemetryStatistics.HasBalanceData(telemetryData, balanceType, context.BalanceOptions))
        {
            return;
        }

        var balance = TelemetryStatistics.CalculateBalance(telemetryData, balanceType, context.BalanceOptions);
        if (balance.AbsoluteSlopeDeltaPercent < thresholdPercent)
        {
            return;
        }

        var frontMagnitude = Math.Abs(balance.FrontSlope);
        var rearMagnitude = Math.Abs(balance.RearSlope);
        var slowerSnapshot = frontMagnitude >= rearMagnitude ? rear : front;
        var severity = balance.AbsoluteSlopeDeltaPercent >= thresholdPercent * 1.5
            ? SessionAnalysisSeverity.Action
            : SessionAnalysisSeverity.Watch;
        var deltaMeasurement = balanceType == BalanceType.Rebound
            ? DiagnosticMeasurement.ReboundBalanceSlopeDeltaPercent
            : DiagnosticMeasurement.CompressionBalanceSlopeDeltaPercent;
        IReadOnlyList<DiagnosticAdjustment> adjustments = hasLimitedBalanceContext
            ? []
            : [CreateBalanceAdjustment(balanceType, slowerSnapshot, context)];
        findings.Add(new DiagnosticFinding(
            SessionAnalysisFindingId.BalanceSlopesDiverge,
            SessionAnalysisCategory.Balance,
            severity,
            context.Request.AnalysisRange is null ? SessionAnalysisConfidence.Low : SessionAnalysisConfidence.Medium,
            [
                new DiagnosticEvidence(deltaMeasurement, null, balance.AbsoluteSlopeDeltaPercent),
                new DiagnosticEvidence(DiagnosticMeasurement.FrontBalanceSlope, SuspensionType.Front, balance.FrontSlope),
                new DiagnosticEvidence(DiagnosticMeasurement.RearBalanceSlope, SuspensionType.Rear, balance.RearSlope),
                new DiagnosticEvidence(DiagnosticMeasurement.BalanceMeanSignedDeviation, null, balance.MeanSignedDeviation),
            ],
            adjustments));
    }

    private static bool HasAnyBalanceData(TelemetryData telemetryData, AnalysisContext context)
    {
        return TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Rebound, context.BalanceOptions) ||
               TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Compression, context.BalanceOptions);
    }

    private static bool HasLimitedBalanceContext(SideSnapshot front, SideSnapshot rear, AnalysisContext context)
    {
        var hasShallowTravel = IsShallowTravel(front) || IsShallowTravel(rear);
        var hasLowCompressionSpeeds = IsBelowReference(Math.Abs(front.Velocity.Percentile95Compression), context.Profile.Compression) ||
                                      IsBelowReference(Math.Abs(rear.Velocity.Percentile95Compression), context.Profile.Compression);
        var hasLowReboundSpeeds = IsBelowReference(Math.Abs(front.Velocity.Percentile95Rebound), context.Profile.Rebound) ||
                                  IsBelowReference(Math.Abs(rear.Velocity.Percentile95Rebound), context.Profile.Rebound);

        return hasShallowTravel || hasLowCompressionSpeeds || hasLowReboundSpeeds;
    }

    private static IReadOnlyList<DiagnosticEvidence> CreateBalanceContextEvidence(
        SideSnapshot front,
        SideSnapshot rear)
    {
        return
        [
            new DiagnosticEvidence(DiagnosticMeasurement.MaxTravelPercent, front.Type, front.MaxTravelPercent),
            new DiagnosticEvidence(DiagnosticMeasurement.MaxTravelPercent, rear.Type, rear.MaxTravelPercent),
            new DiagnosticEvidence(DiagnosticMeasurement.CompressionP95, front.Type, Math.Abs(front.Velocity.Percentile95Compression)),
            new DiagnosticEvidence(DiagnosticMeasurement.CompressionP95, rear.Type, Math.Abs(rear.Velocity.Percentile95Compression)),
            new DiagnosticEvidence(DiagnosticMeasurement.BalanceContextLimit, null, null),
        ];
    }

    private static void AddVibrationFindings(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        List<DiagnosticFinding> findings)
    {
        var addedComparableVibration = false;
        if (front?.HasStrokeData == true && TelemetryStatistics.HasVibrationData(telemetryData, ImuLocation.Fork))
        {
            var vibration = TelemetryStatistics.CalculateVibration(telemetryData, ImuLocation.Fork, SuspensionType.Front, context.Request.AnalysisRange);
            if (vibration is not null)
            {
                addedComparableVibration = true;
                findings.Add(CreateVibrationContextFinding(SuspensionType.Front, vibration));
            }
        }

        if (rear?.HasStrokeData == true && TelemetryStatistics.HasVibrationData(telemetryData, ImuLocation.Shock))
        {
            var vibration = TelemetryStatistics.CalculateVibration(telemetryData, ImuLocation.Shock, SuspensionType.Rear, context.Request.AnalysisRange);
            if (vibration is not null)
            {
                addedComparableVibration = true;
                findings.Add(CreateVibrationContextFinding(SuspensionType.Rear, vibration));
            }
        }

        if (!addedComparableVibration && telemetryData.ImuData is { Records.Count: > 0 })
        {
            findings.Add(new DiagnosticFinding(
                SessionAnalysisFindingId.VibrationNotUsedForRecommendations,
                SessionAnalysisCategory.Vibration,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                [],
                []));
        }
    }

    private static DiagnosticFinding CreateVibrationContextFinding(
        SuspensionType side,
        VibrationStats vibration)
    {
        return new DiagnosticFinding(
            SessionAnalysisFindingId.VibrationContext,
            SessionAnalysisCategory.Vibration,
            SessionAnalysisSeverity.Info,
            SessionAnalysisConfidence.Low,
            [
                new DiagnosticEvidence(DiagnosticMeasurement.MagicCarpetRatio, side, vibration.MagicCarpet),
                new DiagnosticEvidence(DiagnosticMeasurement.AverageG, side, vibration.AverageGOverall),
                new DiagnosticEvidence(DiagnosticMeasurement.CompressionVibrationPercent, side, vibration.CompressionPercent),
                new DiagnosticEvidence(DiagnosticMeasurement.ReboundVibrationPercent, side, vibration.ReboundPercent),
            ],
            []);
    }

    private static IReadOnlyList<DiagnosticEvidence> CreateTravelEvidence(SideSnapshot side)
    {
        var evidence = new List<DiagnosticEvidence>
        {
            new(DiagnosticMeasurement.MaxTravelPercent, side.Type, side.MaxTravelPercent),
            new(DiagnosticMeasurement.AverageTravelPercent, side.Type, side.AverageTravelPercent),
            new(DiagnosticMeasurement.Bottomouts, side.Type, side.Travel.Bottomouts),
        };

        if (side.MaxTravelPercent is not null)
        {
            evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.HealthyHardSectionReference, side.Type, HealthyHardSegmentTravelPercent));
        }

        return evidence;
    }

    private static IReadOnlyList<DiagnosticEvidence> CreateDampingEvidence(
        SideSnapshot side,
        AnalysisContext context,
        bool includeTravel,
        bool includeDamperBands)
    {
        var evidence = new List<DiagnosticEvidence>
        {
            new(DiagnosticMeasurement.CompressionP95, side.Type, Math.Abs(side.Velocity.Percentile95Compression)),
            new(DiagnosticMeasurement.ReboundP95, side.Type, Math.Abs(side.Velocity.Percentile95Rebound)),
            new(DiagnosticMeasurement.CompressionReferenceBand, side.Type, null),
            new(DiagnosticMeasurement.ReboundReferenceBand, side.Type, null),
        };

        if (includeTravel)
        {
            evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.MaxTravelPercent, side.Type, side.MaxTravelPercent));
            evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.AverageTravelPercent, side.Type, side.AverageTravelPercent));
            evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.Bottomouts, side.Type, side.Travel.Bottomouts));
        }

        if (includeDamperBands)
        {
            AddDamperBandEvidence(evidence, side, context.Request.DamperPercentages);
        }

        return evidence;
    }

    private static void AddDamperBandEvidence(
        List<DiagnosticEvidence> evidence,
        SideSnapshot side,
        SessionDamperPercentages percentages)
    {
        evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.DamperBandHsc, side.Type, percentages.Get(side.Type, DamperBand.Hsc)));
        evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.DamperBandLsc, side.Type, percentages.Get(side.Type, DamperBand.Lsc)));
        evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.DamperBandHsr, side.Type, percentages.Get(side.Type, DamperBand.Hsr)));
        evidence.Add(new DiagnosticEvidence(DiagnosticMeasurement.DamperBandLsr, side.Type, percentages.Get(side.Type, DamperBand.Lsr)));
    }

    private static DiagnosticAdjustment CreateBalanceAdjustment(
        BalanceType balanceType,
        SideSnapshot slowerSide,
        AnalysisContext context)
    {
        if (balanceType == BalanceType.Rebound)
        {
            var reboundP95 = Math.Abs(slowerSide.Velocity.Percentile95Rebound);
            return new DiagnosticAdjustment(
                SelectBalanceReboundComponent(slowerSide, context),
                IsBelowReference(reboundP95, context.Profile.Rebound)
                    ? AdjustmentDirection.Open
                    : AdjustmentDirection.Close,
                slowerSide.Type,
                1);
        }

        var compressionP95 = Math.Abs(slowerSide.Velocity.Percentile95Compression);
        return new DiagnosticAdjustment(
            SelectBalanceCompressionComponent(slowerSide, context),
            IsBelowReference(compressionP95, context.Profile.Compression)
                ? AdjustmentDirection.Open
                : AdjustmentDirection.Close,
            slowerSide.Type,
            1);
    }

    private static AdjustmentComponent SelectCompressionOpenComponent(SideSnapshot side, AnalysisContext context)
    {
        var hsc = context.Request.DamperPercentages.Get(side.Type, DamperBand.Hsc) ?? 0;
        var lsc = context.Request.DamperPercentages.Get(side.Type, DamperBand.Lsc) ?? 0;
        return hsc > lsc
            ? AdjustmentComponent.HighSpeedCompression
            : AdjustmentComponent.LowSpeedCompression;
    }

    private static AdjustmentComponent SelectBalanceReboundComponent(SideSnapshot side, AnalysisContext context)
    {
        return context.Request.BalanceSpeedMode switch
        {
            BalanceSpeedMode.HighSpeed => AdjustmentComponent.HighSpeedRebound,
            BalanceSpeedMode.LowSpeed => AdjustmentComponent.LowSpeedRebound,
            _ => SelectDominantReboundComponent(side, context),
        };
    }

    private static AdjustmentComponent SelectBalanceCompressionComponent(SideSnapshot side, AnalysisContext context)
    {
        return context.Request.BalanceSpeedMode switch
        {
            BalanceSpeedMode.HighSpeed => AdjustmentComponent.HighSpeedCompression,
            BalanceSpeedMode.LowSpeed => AdjustmentComponent.LowSpeedCompression,
            _ => SelectDominantCompressionComponent(side, context),
        };
    }

    private static AdjustmentComponent SelectDominantReboundComponent(SideSnapshot side, AnalysisContext context)
    {
        var hsr = context.Request.DamperPercentages.Get(side.Type, DamperBand.Hsr) ?? 0;
        var lsr = context.Request.DamperPercentages.Get(side.Type, DamperBand.Lsr) ?? 0;
        return hsr > lsr
            ? AdjustmentComponent.HighSpeedRebound
            : AdjustmentComponent.LowSpeedRebound;
    }

    private static AdjustmentComponent SelectDominantCompressionComponent(SideSnapshot side, AnalysisContext context)
    {
        var hsc = context.Request.DamperPercentages.Get(side.Type, DamperBand.Hsc) ?? 0;
        var lsc = context.Request.DamperPercentages.Get(side.Type, DamperBand.Lsc) ?? 0;
        return hsc > lsc
            ? AdjustmentComponent.HighSpeedCompression
            : AdjustmentComponent.LowSpeedCompression;
    }

    private static Suspension GetSuspension(TelemetryData telemetryData, SuspensionType side)
    {
        return side == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
    }

    private static double? ToPercent(double value, double? maxTravel)
    {
        return maxTravel is > 0 ? value / maxTravel.Value * 100.0 : null;
    }

    private static bool IsBelowReference(double value, SpeedBand band)
    {
        return value > 0 && value < band.Low;
    }

    private static bool IsAboveFastReference(double value, SpeedBand band)
    {
        return value > band.High * VeryFastMultiplier;
    }

    private static bool IsShallowTravel(SideSnapshot side)
    {
        return side.MaxTravelPercent is not null && side.MaxTravelPercent < ShallowMaxTravelPercent;
    }

    private static SessionAnalysisSeverity? GetBottomoutSeverity(SideSnapshot side)
    {
        if (side.Travel.Bottomouts < RepeatedBottomoutCount)
        {
            return null;
        }

        return side.Travel.Bottomouts >= ChronicBottomoutCount
            ? SessionAnalysisSeverity.Action
            : SessionAnalysisSeverity.Watch;
    }

    private static SessionAnalysisConfidence CreateConfidence(AnalysisContext context, params SideSnapshot?[] sides)
    {
        if (context.Request.AnalysisRange is null)
        {
            return SessionAnalysisConfidence.Low;
        }

        if (sides.Any(side => side is null || side.StrokeCount < MinimumUsefulStrokeCount))
        {
            return SessionAnalysisConfidence.Low;
        }

        return SessionAnalysisConfidence.Medium;
    }
}
