using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public sealed class SessionAnalysisService : ISessionAnalysisService
{
    private const double ShortRangeSeconds = 15.0;
    private const int MinimumUsefulStrokeCount = 8;
    private const double ShallowMaxTravelPercent = 60.0;
    private const double HealthyHardSegmentTravelPercent = 85.0;
    private const double DeepAverageTravelPercent = 45.0;
    private const double HighAverageTravelPercent = 55.0;
    private const double DynamicSagMismatchWatchPercent = 15.0;
    private const int RepeatedBottomoutCount = 3;
    private const int ChronicBottomoutCount = 6;
    private const double CompressionBalanceSlopeWatchPercent = 10.0;
    private const double ReboundBalanceSlopeWatchPercent = 20.0;
    private const double VeryFastMultiplier = 1.25;

    public SessionAnalysisResult Analyze(SessionAnalysisRequest request)
    {
        if (request.TelemetryData is null)
        {
            return SessionAnalysisResult.Hidden;
        }

        var telemetryData = request.TelemetryData;
        var travelOptions = new TravelStatisticsOptions(request.AnalysisRange, request.TravelHistogramMode);
        var velocityOptions = new VelocityStatisticsOptions(request.AnalysisRange, request.VelocityAverageMode);
        var balanceOptions = new BalanceStatisticsOptions(request.AnalysisRange, request.BalanceDisplacementMode, request.BalanceSpeedMode);
        var context = new AnalysisContext(request, travelOptions, velocityOptions, balanceOptions, GetProfileReferences(request.TargetProfile));
        var findings = new List<SessionAnalysisFinding>();

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

        var orderedFindings = findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Category)
            .ToArray();

        return new SessionAnalysisResult(
            SurfacePresentationState.Ready,
            BuildSteps(telemetryData, front, rear, context, orderedFindings),
            orderedFindings.Where(finding => finding.Category == SessionAnalysisCategory.DataQuality).ToArray(),
            BuildVibrationPanel(telemetryData, front, rear, context),
            orderedFindings);
    }

    private static IReadOnlyList<SessionAnalysisStep> BuildSteps(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        IReadOnlyList<SessionAnalysisFinding> findings)
    {
        return
        [
            BuildStep(
                SessionAnalysisStepId.Sag,
                "Sag & travel use",
                BuildSagMetrics(front, rear),
                findings.Where(IsSagFinding).ToArray()),
            BuildStep(
                SessionAnalysisStepId.Fork,
                "Fork",
                BuildSideMetrics(front, context),
                findings.Where(IsForkFinding).ToArray()),
            BuildStep(
                SessionAnalysisStepId.Rear,
                "Rear",
                BuildSideMetrics(rear, context),
                findings.Where(IsRearFinding).ToArray()),
            BuildStep(
                SessionAnalysisStepId.Balance,
                "Balance",
                BuildBalanceMetrics(telemetryData, context),
                findings.Where(IsBalanceStepFinding).ToArray()),
        ];
    }

    private static SessionAnalysisStep BuildStep(
        SessionAnalysisStepId id,
        string title,
        IReadOnlyList<SessionAnalysisMetric> metrics,
        IReadOnlyList<SessionAnalysisFinding> findings)
    {
        var hasIssue = findings.Any(finding => finding.Severity != SessionAnalysisSeverity.Info);
        var verdict = hasIssue
            ? findings.Max(finding => finding.Severity)
            : SessionAnalysisSeverity.Info;
        var candidates = findings
            .SelectMany(finding => finding.Adjustments.Select(adjustment => new AdjustmentCandidate(
                adjustment,
                finding.Severity,
                finding.Confidence)))
            .OrderByDescending(candidate => candidate.Severity)
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Adjustment.Priority)
            .ToArray();
        var primary = candidates.FirstOrDefault()?.Adjustment;
        var alternates = candidates
            .Skip(primary is null ? 0 : 1)
            .Select(candidate => candidate.Adjustment)
            .Where(adjustment => primary is null || !IsSameAdjustment(primary, adjustment))
            .DistinctBy(adjustment => (adjustment.Component, adjustment.Direction, adjustment.Side))
            .ToArray();

        return new SessionAnalysisStep(id, title, verdict, hasIssue, metrics, primary, alternates, findings);
    }

    private static bool IsSagFinding(SessionAnalysisFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.TravelUse ||
               (finding.Category == SessionAnalysisCategory.Balance &&
                finding.Id == SessionAnalysisFindingId.DynamicSagMismatch);
    }

    private static bool IsForkFinding(SessionAnalysisFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.ForkDamping ||
               (finding.Category == SessionAnalysisCategory.Packing && FindingSide(finding) == "Fork");
    }

    private static bool IsRearFinding(SessionAnalysisFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.RearDamping ||
               (finding.Category == SessionAnalysisCategory.Packing && FindingSide(finding) == "Rear");
    }

    private static bool IsBalanceStepFinding(SessionAnalysisFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.Balance &&
               finding.Id != SessionAnalysisFindingId.DynamicSagMismatch;
    }

    private static string? FindingSide(SessionAnalysisFinding finding)
    {
        return finding.Evidence.FirstOrDefault(evidence => evidence.Side is "Fork" or "Rear")?.Side;
    }

    private static bool IsSameAdjustment(Adjustment left, Adjustment right)
    {
        return left.Component == right.Component &&
               left.Direction == right.Direction &&
               left.Side == right.Side;
    }

    private static IReadOnlyList<SessionAnalysisMetric> BuildSagMetrics(
        SideSnapshot? front,
        SideSnapshot? rear)
    {
        var metrics = new List<SessionAnalysisMetric>
        {
            CreateMetric("Fork max", FormatNullablePercent(front?.MaxTravelPercent), "%", "Fork", ">= 85 % on hard terrain"),
            CreateMetric("Fork avg", FormatNullablePercent(front?.AverageTravelPercent), "%", "Fork", "compare against baseline"),
            CreateMetric("Rear max", FormatNullablePercent(rear?.MaxTravelPercent), "%", "Rear", ">= 85 % on hard terrain"),
            CreateMetric("Rear avg", FormatNullablePercent(rear?.AverageTravelPercent), "%", "Rear", "compare against baseline"),
        };

        if (front?.AverageTravelPercent is { } frontAverage && rear?.AverageTravelPercent is { } rearAverage)
        {
            metrics.Add(CreateMetric(
                "Avg delta",
                FormatNumber(Math.Abs(frontAverage - rearAverage), 1),
                "%",
                null,
                "front/rear average within ~10-15 %"));
        }

        return metrics;
    }

    private static IReadOnlyList<SessionAnalysisMetric> BuildSideMetrics(
        SideSnapshot? side,
        AnalysisContext context)
    {
        if (side is null)
        {
            return
            [
                CreateMetric("Comp 95th", "n/a", "mm/s", null, FormatBand(context.Profile.Compression)),
                CreateMetric("Reb 95th", "n/a", "mm/s", null, FormatBand(context.Profile.Rebound)),
                CreateMetric("Max travel", "n/a", "%", null, ">= 85 % on hard terrain"),
            ];
        }

        return
        [
            CreateMetric("Comp 95th", FormatSpeed(Math.Abs(side.Velocity.Percentile95Compression)), "mm/s", side.Name, FormatBand(context.Profile.Compression)),
            CreateMetric("Reb 95th", FormatSpeed(Math.Abs(side.Velocity.Percentile95Rebound)), "mm/s", side.Name, FormatBand(context.Profile.Rebound)),
            CreateMetric("Max travel", FormatNullablePercent(side.MaxTravelPercent), "%", side.Name, ">= 85 % on hard terrain"),
        ];
    }

    private static IReadOnlyList<SessionAnalysisMetric> BuildBalanceMetrics(
        TelemetryData telemetryData,
        AnalysisContext context)
    {
        return
        [
            CreateMetric(
                "Compression slope delta",
                FormatBalanceDelta(telemetryData, BalanceType.Compression, context),
                "%",
                null,
                "< 10 %"),
            CreateMetric(
                "Rebound slope delta",
                FormatBalanceDelta(telemetryData, BalanceType.Rebound, context),
                "%",
                null,
                "< 20 %"),
        ];
    }

    private static string FormatBalanceDelta(
        TelemetryData telemetryData,
        BalanceType balanceType,
        AnalysisContext context)
    {
        if (!TelemetryStatistics.HasBalanceData(telemetryData, balanceType, context.BalanceOptions))
        {
            return "n/a";
        }

        var balance = TelemetryStatistics.CalculateBalance(telemetryData, balanceType, context.BalanceOptions);
        return FormatNumber(balance.AbsoluteSlopeDeltaPercent, 1);
    }

    private static SessionAnalysisVibrationPanel? BuildVibrationPanel(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context)
    {
        var metrics = new List<SessionAnalysisMetric>();
        AddVibrationMetrics(metrics, telemetryData, front, ImuLocation.Fork, SuspensionType.Front, "Fork", context);
        AddVibrationMetrics(metrics, telemetryData, rear, ImuLocation.Shock, SuspensionType.Rear, "Rear", context);

        return metrics.Count == 0
            ? null
            : new SessionAnalysisVibrationPanel(
                metrics,
                "Compare vibration only side-by-side on the same trail at the same pace.");
    }

    private static void AddVibrationMetrics(
        List<SessionAnalysisMetric> metrics,
        TelemetryData telemetryData,
        SideSnapshot? side,
        ImuLocation imuLocation,
        SuspensionType suspensionType,
        string sideName,
        AnalysisContext context)
    {
        if (side?.HasStrokeData != true || !TelemetryStatistics.HasVibrationData(telemetryData, imuLocation))
        {
            return;
        }

        var vibration = TelemetryStatistics.CalculateVibration(telemetryData, imuLocation, suspensionType, context.Request.AnalysisRange);
        if (vibration is null)
        {
            return;
        }

        metrics.Add(CreateMetric("Magic carpet ratio", FormatNumber(vibration.MagicCarpet, 2), null, sideName, null));
        metrics.Add(CreateMetric("Average g", FormatNumber(vibration.AverageGOverall, 2), "g", sideName, null));
        metrics.Add(CreateMetric("Compression vibration", FormatNumber(vibration.CompressionPercent, 1), "%", sideName, null));
        metrics.Add(CreateMetric("Rebound vibration", FormatNumber(vibration.ReboundPercent, 1), "%", sideName, null));
    }

    private static SessionAnalysisMetric CreateMetric(
        string label,
        string value,
        string? unit,
        string? side,
        string? targetRange)
    {
        return new SessionAnalysisMetric(label, value, unit, side, targetRange);
    }

    private static void AddDataQualityFindings(
        TelemetryData telemetryData,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings)
    {
        if (context.Request.AnalysisRange is null)
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.FullSessionAnalysis,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Low,
                "Full session analysis",
                "The analysis is using the full session, so climbs, flats, stops, and transfers may be mixed into the suspension statistics.",
                "Select the difficult downhill section before making tuning changes from these findings.",
                [CreateEvidence("Analysis range", "Full session", null, null, "Selected range")]));
        }
        else if (context.Request.AnalysisRange.Value.DurationSeconds < ShortRangeSeconds)
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.ShortSelectedRange,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Low,
                "Short selected range",
                "The selected section is short, so a few events can dominate the statistics.",
                "Use a longer representative section if the trail allows, then compare the same segment after changes.",
                [CreateEvidence("Range duration", FormatNumber(context.Request.AnalysisRange.Value.DurationSeconds, 1), "s", null, "Selected range")]));
        }

        var frontHasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Front, context.Request.AnalysisRange);
        var rearHasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, SuspensionType.Rear, context.Request.AnalysisRange);
        if (!frontHasStrokeData && !rearHasStrokeData)
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.NoStrokeStatistics,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Low,
                "No stroke statistics",
                "The selected data does not contain usable compression or rebound strokes.",
                "Pick a section with clear suspension movement before interpreting damping or balance.",
                []));
            return;
        }

        if (frontHasStrokeData != rearHasStrokeData)
        {
            var missingSide = frontHasStrokeData ? "rear" : "front";
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.OneEndMissingStrokeData,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                "One end is missing stroke data",
                $"The {missingSide} side does not have usable strokes in this selection, so front/rear balance recommendations are skipped.",
                "Use a section where both ends of the bike are active if you want balance guidance.",
                []));
        }

        if (!TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Compression, context.BalanceOptions) &&
            !TelemetryStatistics.HasBalanceData(telemetryData, BalanceType.Rebound, context.BalanceOptions))
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.BalanceUnavailable,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                "Balance unavailable",
                "Balance needs enough front and rear compression or rebound events to fit trend lines.",
                "Use a rougher or longer section before acting on front/rear balance.",
                [CreateEvidence("Balance mode", ModeLabel(context.Request.BalanceDisplacementMode), null, null, "Selected statistics mode")]));
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
            side == SuspensionType.Front ? "Fork" : "Rear",
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
        List<SessionAnalysisFinding> findings)
    {
        if (side is null || !side.HasStrokeData)
        {
            return;
        }

        if (side.StrokeCount < MinimumUsefulStrokeCount)
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.LowStrokeCount,
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                $"{side.Name} stroke count is low",
                $"Only {side.StrokeCount} {side.Name.ToLowerInvariant()} strokes are available in the selected data.",
                "Treat damping recommendations as context until the same section has more usable events.",
                [CreateEvidence("Stroke count", side.StrokeCount.ToString(CultureInfo.InvariantCulture), null, side.Name, ModeLabel(context.Request.VelocityAverageMode))]));
        }

        if (side.MaxTravelPercent is not null && side.MaxTravelPercent < ShallowMaxTravelPercent)
        {
            var adjustments = new[]
            {
                CreateAdjustment(
                    AdjustmentComponent.AirPressure,
                    AdjustmentDirection.Remove,
                    "small (~2-5 PSI)",
                    side,
                    "Max travel should rise toward 80-90 % on the same hard section.",
                    1),
                CreateAdjustment(
                    SelectCompressionOpenComponent(side, context),
                    AdjustmentDirection.Open,
                    "1 click",
                    side,
                    "Max travel should rise toward 80-90 % on the same hard section.",
                    2),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.ShallowTravelUse,
                SessionAnalysisCategory.TravelUse,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} travel use is shallow",
                $"The {side.Name.ToLowerInvariant()} only reached {FormatPercent(side.MaxTravelPercent.Value)} of available travel in the selected {TravelModeDescription(context.Request.TravelHistogramMode)} data.",
                BuildRecommendationText(adjustments, "Check pressure or preload and progression before chasing fine damping; try a small change and rerun the same section."),
                CreateTravelEvidence(side, context),
                adjustments));
        }

        var bottomoutSeverity = GetBottomoutSeverity(side);
        if (bottomoutSeverity is not null)
        {
            IReadOnlyList<Adjustment> adjustments = bottomoutSeverity == SessionAnalysisSeverity.Action
                ?
                [
                    CreateAdjustment(
                        AdjustmentComponent.Tokens,
                        AdjustmentDirection.Add,
                        "1 token",
                        side,
                        "End-stroke ramp should resist deep travel; 100 % hits become rare.",
                        1),
                    CreateAdjustment(
                        AdjustmentComponent.AirPressure,
                        AdjustmentDirection.Add,
                        "small (~2-5 PSI)",
                        side,
                        "Repeated bottomouts should reduce; max settles near 90-95 %.",
                        2),
                ]
                :
                [
                    CreateAdjustment(
                        AdjustmentComponent.AirPressure,
                        AdjustmentDirection.Add,
                        "small (~2-5 PSI)",
                        side,
                        "Repeated bottomouts should reduce; max settles near 90-95 %.",
                        1),
                    CreateAdjustment(
                        AdjustmentComponent.Tokens,
                        AdjustmentDirection.Add,
                        "1 token",
                        side,
                        "Repeated bottomouts should reduce; max settles near 90-95 %.",
                        2),
                ];
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.RepeatedBottomouts,
                SessionAnalysisCategory.TravelUse,
                bottomoutSeverity.Value,
                CreateConfidence(context, side),
                $"{side.Name} bottomed repeatedly",
                $"The selected data contains {side.Travel.Bottomouts} {BottomoutObservationName(context.Request.TravelHistogramMode)} on the {side.Name.ToLowerInvariant()} side.",
                BuildRecommendationText(adjustments, bottomoutSeverity == SessionAnalysisSeverity.Action
                    ? "Review spring pressure or preload, end-stroke progression, and compression support before opening damping further."
                    : "Treat this as a support clue and compare against rider notes before changing pressure, progression, or compression support."),
                CreateTravelEvidence(side, context),
                adjustments));
        }

        if (side.AverageTravelPercent is not null &&
            side.AverageTravelPercent > HighAverageTravelPercent &&
            bottomoutSeverity is null)
        {
            var adjustments = new[]
            {
                CreateAdjustment(
                    AdjustmentComponent.AirPressure,
                    AdjustmentDirection.Add,
                    "small (~2-5 PSI)",
                    side,
                    "Average travel should ride 5-10 % shallower; rebound 95th should rise.",
                    1),
                CreateAdjustment(
                    AdjustmentComponent.HighSpeedRebound,
                    AdjustmentDirection.Open,
                    "1 click",
                    side,
                    "Average travel should ride 5-10 % shallower; rebound 95th should rise.",
                    2),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.DeepTravelUse,
                SessionAnalysisCategory.TravelUse,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} is riding deep",
                $"The {side.Name.ToLowerInvariant()} average position is {FormatPercent(side.AverageTravelPercent.Value)} in the selected {TravelModeDescription(context.Request.TravelHistogramMode)} data.",
                BuildRecommendationText(adjustments, "Confirm this was a hard descending section, then use packing and balance evidence before deciding between support and rebound changes."),
                CreateTravelEvidence(side, context),
                adjustments));
        }
    }

    private static void AddDynamicSagBalanceFinding(
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings)
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

        var higherSide = delta > 0 ? front.Name : rear.Name;
        var lowerSide = delta > 0 ? rear.Name : front.Name;
        var deeperSide = delta > 0 ? front : rear;
        var shallowerSide = delta > 0 ? rear : front;
        var adjustments = new[]
        {
            CreateAdjustment(
                AdjustmentComponent.AirPressure,
                AdjustmentDirection.Add,
                "small (~2-5 PSI)",
                deeperSide,
                "Front and rear average position should move closer together on the same steep section.",
                1),
            CreateAdjustment(
                AdjustmentComponent.AirPressure,
                AdjustmentDirection.Remove,
                "small (~2-5 PSI)",
                shallowerSide,
                "Front and rear average position should move closer together on the same steep section.",
                2),
        };
        findings.Add(new SessionAnalysisFinding(
            SessionAnalysisFindingId.DynamicSagMismatch,
            SessionAnalysisCategory.Balance,
            SessionAnalysisSeverity.Watch,
            CreateConfidence(context, front, rear),
            "Dynamic sag mismatch",
            $"The {higherSide.ToLowerInvariant()} is averaging deeper than the {lowerSide.ToLowerInvariant()} by {FormatPercent(Math.Abs(delta))} in the selected travel mode.",
            BuildRecommendationText(adjustments, "Treat this as a geometry choice first. If it was not intentional for the terrain, correct travel use before tuning front/rear balance."),
            [
                CreateEvidence("Average travel", FormatNumber(frontAverage, 1), "%", front.Name, ModeLabel(context.Request.TravelHistogramMode)),
                CreateEvidence("Average travel", FormatNumber(rearAverage, 1), "%", rear.Name, ModeLabel(context.Request.TravelHistogramMode)),
            ],
            adjustments));
    }

    private static void AddPackingFindings(
        SideSnapshot? side,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings,
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
            var adjustments = new[]
            {
                CreateAdjustment(
                    AdjustmentComponent.HighSpeedRebound,
                    AdjustmentDirection.Open,
                    "1-2 clicks",
                    side,
                    $"Rebound 95th should rise toward {FormatBand(context.Profile.Rebound)} mm/s and average position should ride shallower.",
                    1),
                CreateAdjustment(
                    AdjustmentComponent.LowSpeedRebound,
                    AdjustmentDirection.Open,
                    "1 click",
                    side,
                    $"Rebound 95th should rise toward {FormatBand(context.Profile.Rebound)} mm/s and average position should ride shallower.",
                    2),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.ReboundPacking,
                SessionAnalysisCategory.Packing,
                SessionAnalysisSeverity.Action,
                CreateConfidence(context, side),
                $"{side.Name} rebound packing is plausible",
                $"The {side.Name.ToLowerInvariant()} is riding deep while rebound speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                BuildRecommendationText(adjustments, "Try one or two clicks faster rebound and repeat the same section, then check whether average position and rider harshness improve."),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                adjustments));
            return;
        }

        if (deepAverage && repeatedBottomouts)
        {
            packingSides.Add(side.Type);
            IReadOnlyList<Adjustment> adjustments = bottomoutSeverity == SessionAnalysisSeverity.Action
                ?
                [
                    CreateAdjustment(
                        AdjustmentComponent.Tokens,
                        AdjustmentDirection.Add,
                        "1 token",
                        side,
                        "End-stroke ramp should resist deep travel; 100 % hits become rare.",
                        1),
                    CreateAdjustment(
                        AdjustmentComponent.AirPressure,
                        AdjustmentDirection.Add,
                        "small (~2-5 PSI)",
                        side,
                        "Repeated bottomouts should reduce; max settles near 90-95 %.",
                        2),
                ]
                :
                [
                    CreateAdjustment(
                        AdjustmentComponent.AirPressure,
                        AdjustmentDirection.Add,
                        "small (~2-5 PSI)",
                        side,
                        "Repeated bottomouts should reduce; max settles near 90-95 %.",
                        1),
                    CreateAdjustment(
                        AdjustmentComponent.Tokens,
                        AdjustmentDirection.Add,
                        "1 token",
                        side,
                        "Repeated bottomouts should reduce; max settles near 90-95 %.",
                        2),
                ];
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.SupportBeforeReboundDiagnosis,
                SessionAnalysisCategory.Packing,
                bottomoutSeverity!.Value,
                CreateConfidence(context, side),
                bottomoutSeverity == SessionAnalysisSeverity.Action
                    ? $"{side.Name} needs support before rebound diagnosis"
                    : $"{side.Name} support needs context before rebound diagnosis",
                $"The {side.Name.ToLowerInvariant()} is riding deep and has {side.Travel.Bottomouts} {BottomoutObservationName(context.Request.TravelHistogramMode)}, so support evidence should be separated from rebound packing before changing rebound.",
                BuildRecommendationText(adjustments, bottomoutSeverity == SessionAnalysisSeverity.Action
                    ? "Address pressure or preload, progression, and compression support before opening rebound to chase a packing feel."
                    : "Repeat the same section or compare rider notes before treating these bottomouts as a chronic support problem."),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                adjustments));
            return;
        }

        if (shallowTravel && lowCompression)
        {
            packingSides.Add(side.Type);
            var adjustments = new[]
            {
                CreateAdjustment(
                    AdjustmentComponent.HighSpeedCompression,
                    AdjustmentDirection.Open,
                    "1-2 clicks",
                    side,
                    "Compression 95th rises into reference and max travel reaches 80-90 %.",
                    1),
                CreateAdjustment(
                    AdjustmentComponent.AirPressure,
                    AdjustmentDirection.Remove,
                    "small (~2-5 PSI)",
                    side,
                    "Compression 95th rises into reference and max travel reaches 80-90 %.",
                    2),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.ResistingImpacts,
                SessionAnalysisCategory.Packing,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} may be resisting impacts",
                $"The {side.Name.ToLowerInvariant()} is not using much travel and compression speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                BuildRecommendationText(adjustments, "Separate spring/progression from damping with a small compression-opening experiment, but avoid larger changes until travel support evidence agrees."),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                adjustments));
        }
    }

    private static void AddSideDampingFindings(
        SideSnapshot? side,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings,
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
            var adjustments = new[]
            {
                CreateAdjustment(
                    AdjustmentComponent.HighSpeedRebound,
                    AdjustmentDirection.Open,
                    "1 click",
                    side,
                    $"Rebound 95th should rise into {FormatBand(context.Profile.Rebound)} mm/s.",
                    1),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.ReboundSlowForProfileContext,
                category,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} rebound is slow for profile context",
                $"The {side.Name.ToLowerInvariant()} rebound 95th percentile speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                BuildRecommendationText(adjustments, "If the rider felt packing or poor recovery, try one click faster rebound and repeat the same section."),
                CreateDampingEvidence(side, context, includeTravel: false, includeDamperBands: true),
                adjustments));
        }
        else if (IsAboveFastReference(reboundP95, context.Profile.Rebound))
        {
            var adjustments = new[]
            {
                CreateAdjustment(
                    AdjustmentComponent.HighSpeedRebound,
                    AdjustmentDirection.Close,
                    "1 click",
                    side,
                    $"Rebound 95th should fall back into {FormatBand(context.Profile.Rebound)} mm/s; the bike should feel less nervous.",
                    1),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.ReboundFastForProfileContext,
                category,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} rebound is fast for profile context",
                $"The {side.Name.ToLowerInvariant()} rebound 95th percentile speed is well above the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                BuildRecommendationText(adjustments, "If the bike felt nervous or kicked back, try one click slower rebound and compare the same section."),
                CreateDampingEvidence(side, context, includeTravel: false, includeDamperBands: true),
                adjustments));
        }

        if (side.Travel.Bottomouts >= RepeatedBottomoutCount)
        {
            return;
        }

        if (IsBelowReference(compressionP95, context.Profile.Compression))
        {
            var adjustments = new[]
            {
                CreateAdjustment(
                    SelectCompressionOpenComponent(side, context),
                    AdjustmentDirection.Open,
                    "1 click",
                    side,
                    "Compression 95th should rise; max travel should rise with it.",
                    1),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.CompressionSpeedsSubdued,
                category,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, side),
                $"{side.Name} compression speeds are subdued",
                $"The {side.Name.ToLowerInvariant()} compression 95th percentile speed is below the {ProfileLabel(context.Request.TargetProfile)} context band.",
                BuildRecommendationText(adjustments, "Use rider feel and travel use before changing compression; this may also reflect smoother terrain or spring/progression."),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                adjustments));
        }
        else if (IsAboveFastReference(compressionP95, context.Profile.Compression))
        {
            var adjustments = new[]
            {
                CreateAdjustment(
                    AdjustmentComponent.HighSpeedCompression,
                    AdjustmentDirection.Close,
                    "1 click",
                    side,
                    "Compression 95th should drop into reference; check that max travel still reaches 80-90 %.",
                    1),
            };
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.CompressionSpeedsHigh,
                category,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, side),
                $"{side.Name} compression speeds are high",
                $"The {side.Name.ToLowerInvariant()} compression 95th percentile speed is above the {ProfileLabel(context.Request.TargetProfile)} context band.",
                BuildRecommendationText(adjustments, "High speeds can simply mean hard terrain. Confirm support and bottomout margin before adding compression damping."),
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true),
                adjustments));
        }
    }

    private static void AddBalanceFindings(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings)
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
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.BalanceContextLimited,
                SessionAnalysisCategory.Balance,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, front, rear),
                "Balance context is limited",
                "The selected data has shallow travel use or low speed context, so any balance reading should be treated as supporting context rather than a tuning verdict.",
                "Resolve travel use and terrain context before treating balance findings as a dialed setup.",
                CreateBalanceContextEvidence(front, rear, context)));
        }
    }

    private static bool AddBalanceFinding(
        TelemetryData telemetryData,
        BalanceType balanceType,
        double thresholdPercent,
        SideSnapshot front,
        SideSnapshot rear,
        bool hasLimitedBalanceContext,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings)
    {
        if (!TelemetryStatistics.HasBalanceData(telemetryData, balanceType, context.BalanceOptions))
        {
            return false;
        }

        var balance = TelemetryStatistics.CalculateBalance(telemetryData, balanceType, context.BalanceOptions);
        if (balance.AbsoluteSlopeDeltaPercent < thresholdPercent)
        {
            return false;
        }

        var frontMagnitude = Math.Abs(balance.FrontSlope);
        var rearMagnitude = Math.Abs(balance.RearSlope);
        var fasterSide = frontMagnitude >= rearMagnitude ? "front" : "rear";
        var slowerSide = frontMagnitude >= rearMagnitude ? "rear" : "front";
        var slowerSnapshot = frontMagnitude >= rearMagnitude ? rear : front;
        var severity = balance.AbsoluteSlopeDeltaPercent >= thresholdPercent * 1.5
            ? SessionAnalysisSeverity.Action
            : SessionAnalysisSeverity.Watch;
        var typeLabel = balanceType == BalanceType.Rebound ? "rebound" : "compression";
        IReadOnlyList<Adjustment> adjustments = hasLimitedBalanceContext
            ? []
            : new[]
            {
                CreateBalanceAdjustment(balanceType, slowerSnapshot, context),
            };
        findings.Add(new SessionAnalysisFinding(
            SessionAnalysisFindingId.BalanceSlopesDiverge,
            SessionAnalysisCategory.Balance,
            severity,
            context.Request.AnalysisRange is null ? SessionAnalysisConfidence.Low : SessionAnalysisConfidence.Medium,
            $"{Capitalize(typeLabel)} balance slopes diverge",
            $"The {fasterSide} {typeLabel} trend is steeper than the {slowerSide} trend by {FormatPercent(balance.AbsoluteSlopeDeltaPercent)} using the selected {ModeLabel(context.Request.BalanceDisplacementMode).ToLowerInvariant()} balance mode.",
            BuildRecommendationText(
                adjustments,
                hasLimitedBalanceContext
                    ? "Resolve travel use / speed context before balance tuning."
                    : "Use this as a front/rear experiment guide only after travel use looks reasonable; change the side whose travel and damping evidence agree with the imbalance."),
            [
                CreateEvidence("Slope delta", FormatNumber(balance.AbsoluteSlopeDeltaPercent, 1), "%", null, ModeLabel(context.Request.BalanceDisplacementMode)),
                CreateEvidence("Front slope", FormatNumber(balance.FrontSlope, 2), null, "Fork", ModeLabel(context.Request.BalanceDisplacementMode)),
                CreateEvidence("Rear slope", FormatNumber(balance.RearSlope, 2), null, "Rear", ModeLabel(context.Request.BalanceDisplacementMode)),
                CreateEvidence("Mean deviation", FormatNumber(balance.MeanSignedDeviation, 1), "mm/s", null, ModeLabel(context.Request.BalanceDisplacementMode)),
            ],
            adjustments));
        return true;
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

    private static IReadOnlyList<SessionAnalysisEvidence> CreateBalanceContextEvidence(
        SideSnapshot front,
        SideSnapshot rear,
        AnalysisContext context)
    {
        return
        [
            CreateEvidence("Max travel", FormatNullablePercent(front.MaxTravelPercent), "%", front.Name, ModeLabel(context.Request.TravelHistogramMode)),
            CreateEvidence("Max travel", FormatNullablePercent(rear.MaxTravelPercent), "%", rear.Name, ModeLabel(context.Request.TravelHistogramMode)),
            CreateEvidence("Compression p95", FormatSpeed(Math.Abs(front.Velocity.Percentile95Compression)), "mm/s", front.Name, ModeLabel(context.Request.VelocityAverageMode)),
            CreateEvidence("Compression p95", FormatSpeed(Math.Abs(rear.Velocity.Percentile95Compression)), "mm/s", rear.Name, ModeLabel(context.Request.VelocityAverageMode)),
            CreateEvidence("Context limit", "travel/speed", null, null, "Balance interpretation"),
        ];
    }

    private static void AddVibrationFindings(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings)
    {
        var addedComparableVibration = false;
        if (front?.HasStrokeData == true && TelemetryStatistics.HasVibrationData(telemetryData, ImuLocation.Fork))
        {
            var vibration = TelemetryStatistics.CalculateVibration(telemetryData, ImuLocation.Fork, SuspensionType.Front, context.Request.AnalysisRange);
            if (vibration is not null)
            {
                addedComparableVibration = true;
                findings.Add(CreateVibrationContextFinding("Fork", "fork IMU", vibration));
            }
        }

        if (rear?.HasStrokeData == true && TelemetryStatistics.HasVibrationData(telemetryData, ImuLocation.Shock))
        {
            var vibration = TelemetryStatistics.CalculateVibration(telemetryData, ImuLocation.Shock, SuspensionType.Rear, context.Request.AnalysisRange);
            if (vibration is not null)
            {
                addedComparableVibration = true;
                findings.Add(CreateVibrationContextFinding("Rear", "shock IMU", vibration));
            }
        }

        if (!addedComparableVibration && telemetryData.ImuData is { Records.Count: > 0 })
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisFindingId.VibrationNotUsedForRecommendations,
                SessionAnalysisCategory.Vibration,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                "Vibration not used for recommendations",
                "The available IMU data is missing a physically comparable travel pairing for this first-pass analysis.",
                "Use vibration as context only until front travel is paired with fork IMU or rear travel is paired with shock IMU.",
                []));
        }
    }

    private static SessionAnalysisFinding CreateVibrationContextFinding(
        string side,
        string imuLabel,
        VibrationStats vibration)
    {
        return new SessionAnalysisFinding(
            SessionAnalysisFindingId.VibrationContext,
            SessionAnalysisCategory.Vibration,
            SessionAnalysisSeverity.Info,
            SessionAnalysisConfidence.Low,
            $"{side} vibration context",
            $"The {side.ToLowerInvariant()} travel can be compared with the {imuLabel}; this is useful mainly against another run on the same section.",
            "Do not tune from vibration alone. Compare magic-carpet ratio, average g, travel use, and rider notes together.",
            [
                CreateEvidence("Magic carpet ratio", FormatNumber(vibration.MagicCarpet, 2), null, side, "Comparable vibration"),
                CreateEvidence("Average g", FormatNumber(vibration.AverageGOverall, 2), "g", side, "Comparable vibration"),
                CreateEvidence("Compression vibration", FormatNumber(vibration.CompressionPercent, 1), "%", side, "Comparable vibration"),
                CreateEvidence("Rebound vibration", FormatNumber(vibration.ReboundPercent, 1), "%", side, "Comparable vibration"),
            ]);
    }

    private static IReadOnlyList<SessionAnalysisEvidence> CreateTravelEvidence(
        SideSnapshot side,
        AnalysisContext context)
    {
        var evidence = new List<SessionAnalysisEvidence>
        {
            CreateEvidence("Max travel", FormatNullablePercent(side.MaxTravelPercent), "%", side.Name, ModeLabel(context.Request.TravelHistogramMode)),
            CreateEvidence("Average travel", FormatNullablePercent(side.AverageTravelPercent), "%", side.Name, ModeLabel(context.Request.TravelHistogramMode)),
            CreateEvidence(BottomoutEvidenceLabel(context.Request.TravelHistogramMode), side.Travel.Bottomouts.ToString(CultureInfo.InvariantCulture), null, side.Name, BottomoutSourceMode(context.Request.TravelHistogramMode)),
        };

        if (side.MaxTravelPercent is not null)
        {
            evidence.Add(CreateEvidence("Healthy hard-section reference", FormatNumber(HealthyHardSegmentTravelPercent, 0), "%", side.Name, "MotionIQ guide context"));
        }

        return evidence;
    }

    private static IReadOnlyList<SessionAnalysisEvidence> CreateDampingEvidence(
        SideSnapshot side,
        AnalysisContext context,
        bool includeTravel,
        bool includeDamperBands)
    {
        var evidence = new List<SessionAnalysisEvidence>
        {
            CreateEvidence("Compression p95", FormatSpeed(Math.Abs(side.Velocity.Percentile95Compression)), "mm/s", side.Name, ModeLabel(context.Request.VelocityAverageMode)),
            CreateEvidence("Rebound p95", FormatSpeed(Math.Abs(side.Velocity.Percentile95Rebound)), "mm/s", side.Name, ModeLabel(context.Request.VelocityAverageMode)),
            CreateEvidence("Compression reference", FormatBand(context.Profile.Compression), "mm/s", side.Name, ProfileLabel(context.Request.TargetProfile)),
            CreateEvidence("Rebound reference", FormatBand(context.Profile.Rebound), "mm/s", side.Name, ProfileLabel(context.Request.TargetProfile)),
        };

        if (includeTravel)
        {
            evidence.Add(CreateEvidence("Max travel", FormatNullablePercent(side.MaxTravelPercent), "%", side.Name, ModeLabel(context.Request.TravelHistogramMode)));
            evidence.Add(CreateEvidence("Average travel", FormatNullablePercent(side.AverageTravelPercent), "%", side.Name, ModeLabel(context.Request.TravelHistogramMode)));
            evidence.Add(CreateEvidence(BottomoutEvidenceLabel(context.Request.TravelHistogramMode), side.Travel.Bottomouts.ToString(CultureInfo.InvariantCulture), null, side.Name, BottomoutSourceMode(context.Request.TravelHistogramMode)));
        }

        if (includeDamperBands)
        {
            AddDamperBandEvidence(evidence, side, context.Request.DamperPercentages);
        }

        return evidence;
    }

    private static void AddDamperBandEvidence(
        List<SessionAnalysisEvidence> evidence,
        SideSnapshot side,
        SessionDamperPercentages percentages)
    {
        var source = "Current damper band percentages";
        evidence.Add(CreateEvidence("HSC", FormatNullablePercent(GetDamperPercentage(percentages, side.Type, DamperBand.Hsc)), "%", side.Name, source));
        evidence.Add(CreateEvidence("LSC", FormatNullablePercent(GetDamperPercentage(percentages, side.Type, DamperBand.Lsc)), "%", side.Name, source));
        evidence.Add(CreateEvidence("HSR", FormatNullablePercent(GetDamperPercentage(percentages, side.Type, DamperBand.Hsr)), "%", side.Name, source));
        evidence.Add(CreateEvidence("LSR", FormatNullablePercent(GetDamperPercentage(percentages, side.Type, DamperBand.Lsr)), "%", side.Name, source));
    }

    private static double? GetDamperPercentage(
        SessionDamperPercentages percentages,
        SuspensionType side,
        DamperBand band)
    {
        return (side, band) switch
        {
            (SuspensionType.Front, DamperBand.Hsc) => percentages.FrontHscPercentage,
            (SuspensionType.Front, DamperBand.Lsc) => percentages.FrontLscPercentage,
            (SuspensionType.Front, DamperBand.Hsr) => percentages.FrontHsrPercentage,
            (SuspensionType.Front, DamperBand.Lsr) => percentages.FrontLsrPercentage,
            (SuspensionType.Rear, DamperBand.Hsc) => percentages.RearHscPercentage,
            (SuspensionType.Rear, DamperBand.Lsc) => percentages.RearLscPercentage,
            (SuspensionType.Rear, DamperBand.Hsr) => percentages.RearHsrPercentage,
            (SuspensionType.Rear, DamperBand.Lsr) => percentages.RearLsrPercentage,
            _ => null,
        };
    }

    private static SessionAnalysisEvidence CreateEvidence(
        string label,
        string value,
        string? unit,
        string? side,
        string sourceMode,
        string? note = null)
    {
        return new SessionAnalysisEvidence(label, value, unit, side, sourceMode, note);
    }

    private static Adjustment CreateAdjustment(
        AdjustmentComponent component,
        AdjustmentDirection direction,
        string magnitude,
        SideSnapshot side,
        string expectedEffect,
        int priority)
    {
        return new Adjustment(component, direction, magnitude, side.Name, expectedEffect, priority);
    }

    private static string BuildRecommendationText(
        IReadOnlyList<Adjustment> adjustments,
        string fallbackRecommendation)
    {
        if (adjustments.Count == 0)
        {
            return fallbackRecommendation;
        }

        var primary = adjustments[0];
        var text = primary.SentenceText;
        if (adjustments.Count > 1)
        {
            text += $"; consider {BuildAdjustmentActionPhrase(adjustments[1])}";
        }

        return $"{text}. Expected: {primary.ExpectedEffect}";
    }

    private static string BuildAdjustmentActionPhrase(Adjustment adjustment)
    {
        var verb = adjustment.DirectionVerb.ToLowerInvariant();
        return adjustment.Component switch
        {
            AdjustmentComponent.AirPressure or AdjustmentComponent.Preload =>
                $"{verb} {adjustment.ComponentName}, {adjustment.Magnitude}",
            AdjustmentComponent.Tokens =>
                $"{verb} {adjustment.Magnitude}",
            _ =>
                $"{verb} {adjustment.ComponentName} by {adjustment.Magnitude}",
        };
    }

    private static AdjustmentComponent SelectCompressionOpenComponent(SideSnapshot side, AnalysisContext context)
    {
        var hsc = GetDamperPercentage(context.Request.DamperPercentages, side.Type, DamperBand.Hsc) ?? 0;
        var lsc = GetDamperPercentage(context.Request.DamperPercentages, side.Type, DamperBand.Lsc) ?? 0;
        return hsc > lsc
            ? AdjustmentComponent.HighSpeedCompression
            : AdjustmentComponent.LowSpeedCompression;
    }

    private static Adjustment CreateBalanceAdjustment(
        BalanceType balanceType,
        SideSnapshot slowerSide,
        AnalysisContext context)
    {
        if (balanceType == BalanceType.Rebound)
        {
            var reboundP95 = Math.Abs(slowerSide.Velocity.Percentile95Rebound);
            return CreateAdjustment(
                SelectBalanceReboundComponent(slowerSide, context),
                IsBelowReference(reboundP95, context.Profile.Rebound)
                    ? AdjustmentDirection.Open
                    : AdjustmentDirection.Close,
                "1 click",
                slowerSide,
                "Rebound slope delta should drop under 20 % without making travel use worse.",
                1);
        }

        var compressionP95 = Math.Abs(slowerSide.Velocity.Percentile95Compression);
        return CreateAdjustment(
            SelectBalanceCompressionComponent(slowerSide, context),
            IsBelowReference(compressionP95, context.Profile.Compression)
                ? AdjustmentDirection.Open
                : AdjustmentDirection.Close,
            "1 click",
            slowerSide,
            "Compression slope delta should drop under 10 % without increasing bottomouts.",
            1);
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
        var hsr = GetDamperPercentage(context.Request.DamperPercentages, side.Type, DamperBand.Hsr) ?? 0;
        var lsr = GetDamperPercentage(context.Request.DamperPercentages, side.Type, DamperBand.Lsr) ?? 0;
        return hsr > lsr
            ? AdjustmentComponent.HighSpeedRebound
            : AdjustmentComponent.LowSpeedRebound;
    }

    private static AdjustmentComponent SelectDominantCompressionComponent(SideSnapshot side, AnalysisContext context)
    {
        var hsc = GetDamperPercentage(context.Request.DamperPercentages, side.Type, DamperBand.Hsc) ?? 0;
        var lsc = GetDamperPercentage(context.Request.DamperPercentages, side.Type, DamperBand.Lsc) ?? 0;
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

    private static ProfileReferences GetProfileReferences(SessionAnalysisTargetProfile profile)
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

    private static string TravelModeDescription(TravelHistogramMode mode)
    {
        return mode == TravelHistogramMode.DynamicSag ? "dynamic-sag" : "active-stroke";
    }

    private static string ModeLabel(TravelHistogramMode mode)
    {
        return mode == TravelHistogramMode.DynamicSag ? "Dynamic sag travel stats" : "Active suspension travel stats";
    }

    private static string ModeLabel(VelocityAverageMode mode)
    {
        return mode == VelocityAverageMode.StrokePeakAveraged ? "Stroke-peak average velocity" : "Sample-averaged velocity";
    }

    private static string ModeLabel(BalanceDisplacementMode mode)
    {
        return mode == BalanceDisplacementMode.Travel ? "Travel balance" : "Zenith balance";
    }

    private static string ModeLabel(BalanceSpeedMode mode)
    {
        return mode switch
        {
            BalanceSpeedMode.LowSpeed => "low-speed",
            BalanceSpeedMode.HighSpeed => "high-speed",
            _ => "all-speed",
        };
    }

    private static string BottomoutEvidenceLabel(TravelHistogramMode mode)
    {
        return mode == TravelHistogramMode.DynamicSag ? "Bottomout windows" : "Stroke bottomouts";
    }

    private static string BottomoutSourceMode(TravelHistogramMode mode)
    {
        return mode == TravelHistogramMode.DynamicSag ? "Dynamic sag bottomout windows" : "Active suspension stroke bottomouts";
    }

    private static string BottomoutObservationName(TravelHistogramMode mode)
    {
        return mode == TravelHistogramMode.DynamicSag ? "bottomout windows" : "stroke bottomouts";
    }

    private static string ProfileLabel(SessionAnalysisTargetProfile profile)
    {
        return profile switch
        {
            SessionAnalysisTargetProfile.DH => "DH profile",
            _ => $"{profile} profile",
        };
    }

    private static string FormatBand(SpeedBand band)
    {
        return $"{FormatSpeed(band.Low)}-{FormatSpeed(band.High)}";
    }

    private static string FormatSpeed(double value)
    {
        return FormatNumber(value, 0);
    }

    private static string FormatPercent(double value)
    {
        return $"{FormatNumber(value, 1)}%";
    }

    private static string FormatNullablePercent(double? value)
    {
        return value is null ? "n/a" : FormatNumber(value.Value, 1);
    }

    private static string FormatNumber(double value, int decimals)
    {
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static string Capitalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private enum DamperBand
    {
        Hsc,
        Lsc,
        Hsr,
        Lsr,
    }

    private readonly record struct SpeedBand(double Low, double High);

    private readonly record struct ProfileReferences(SpeedBand Rebound, SpeedBand Compression);

    private sealed record AdjustmentCandidate(
        Adjustment Adjustment,
        SessionAnalysisSeverity Severity,
        SessionAnalysisConfidence Confidence);

    private sealed record AnalysisContext(
        SessionAnalysisRequest Request,
        TravelStatisticsOptions TravelOptions,
        VelocityStatisticsOptions VelocityOptions,
        BalanceStatisticsOptions BalanceOptions,
        ProfileReferences Profile);

    private sealed record SideSnapshot(
        SuspensionType Type,
        string Name,
        bool HasStrokeData,
        TravelStatistics Travel,
        VelocityStatistics Velocity,
        double? MaxTravel,
        double? MaxTravelPercent,
        double? AverageTravelPercent,
        int StrokeCount);
}
