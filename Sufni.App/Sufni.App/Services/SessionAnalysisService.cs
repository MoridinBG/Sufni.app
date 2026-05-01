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
        var balanceOptions = new BalanceStatisticsOptions(request.AnalysisRange, request.BalanceDisplacementMode);
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

        if (findings.Count == 0)
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                "No strong finding",
                "The selected data did not cross the first-pass analysis thresholds.",
                "Use this as a baseline and compare against another run on the same section.",
                []));
        }

        return new SessionAnalysisResult(
            SurfacePresentationState.Ready,
            findings
                .OrderByDescending(finding => finding.Severity)
                .ThenBy(finding => finding.Category)
                .ToArray());
    }

    private static void AddDataQualityFindings(
        TelemetryData telemetryData,
        AnalysisContext context,
        List<SessionAnalysisFinding> findings)
    {
        if (context.Request.AnalysisRange is null)
        {
            findings.Add(new SessionAnalysisFinding(
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
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Watch,
                SessionAnalysisConfidence.Low,
                "Short selected range",
                "The selected section is short, so a few events can dominate the statistics.",
                "Use a longer representative section if the trail allows, then compare the same segment after changes.",
                [CreateEvidence("Range duration", FormatNumber(context.Request.AnalysisRange.Value.DurationSeconds, 1), "s", null, "Selected range")]));
        }

        var frontHasStrokeData = telemetryData.HasStrokeData(SuspensionType.Front, context.Request.AnalysisRange);
        var rearHasStrokeData = telemetryData.HasStrokeData(SuspensionType.Rear, context.Request.AnalysisRange);
        if (!frontHasStrokeData && !rearHasStrokeData)
        {
            findings.Add(new SessionAnalysisFinding(
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
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                "One end is missing stroke data",
                $"The {missingSide} side does not have usable strokes in this selection, so front/rear balance recommendations are skipped.",
                "Use a section where both ends of the bike are active if you want balance guidance.",
                []));
        }

        if (!telemetryData.HasBalanceData(BalanceType.Compression, context.BalanceOptions) &&
            !telemetryData.HasBalanceData(BalanceType.Rebound, context.BalanceOptions))
        {
            findings.Add(new SessionAnalysisFinding(
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

        var hasStrokeData = telemetryData.HasStrokeData(side, context.Request.AnalysisRange);
        var travelStatistics = telemetryData.CalculateTravelStatistics(side, context.TravelOptions);
        var velocityStatistics = telemetryData.CalculateVelocityStatistics(side, context.VelocityOptions);
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
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisCategory.TravelUse,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} travel use is shallow",
                $"The {side.Name.ToLowerInvariant()} only reached {FormatPercent(side.MaxTravelPercent.Value)} of available travel in the selected {TravelModeDescription(context.Request.TravelHistogramMode)} data.",
                "Check pressure or preload and progression before chasing fine damping; try a small change and rerun the same section.",
                CreateTravelEvidence(side, context)));
        }

        var bottomoutSeverity = GetBottomoutSeverity(side);
        if (bottomoutSeverity is not null)
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisCategory.TravelUse,
                bottomoutSeverity.Value,
                CreateConfidence(context, side),
                $"{side.Name} bottomed repeatedly",
                $"The selected data contains {side.Travel.Bottomouts} {BottomoutObservationName(context.Request.TravelHistogramMode)} on the {side.Name.ToLowerInvariant()} side.",
                bottomoutSeverity == SessionAnalysisSeverity.Action
                    ? "Review spring pressure or preload, end-stroke progression, and compression support before opening damping further."
                    : "Treat this as a support clue and compare against rider notes before changing pressure, progression, or compression support.",
                CreateTravelEvidence(side, context)));
        }

        if (side.AverageTravelPercent is not null &&
            side.AverageTravelPercent > HighAverageTravelPercent &&
            bottomoutSeverity is null)
        {
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisCategory.TravelUse,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} is riding deep",
                $"The {side.Name.ToLowerInvariant()} average position is {FormatPercent(side.AverageTravelPercent.Value)} in the selected {TravelModeDescription(context.Request.TravelHistogramMode)} data.",
                "Confirm this was a hard descending section, then use packing and balance evidence before deciding between support and rebound changes.",
                CreateTravelEvidence(side, context)));
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
        findings.Add(new SessionAnalysisFinding(
            SessionAnalysisCategory.Balance,
            SessionAnalysisSeverity.Watch,
            CreateConfidence(context, front, rear),
            "Dynamic sag mismatch",
            $"The {higherSide.ToLowerInvariant()} is averaging deeper than the {lowerSide.ToLowerInvariant()} by {FormatPercent(Math.Abs(delta))} in the selected travel mode.",
            "Treat this as a geometry choice first. If it was not intentional for the terrain, correct travel use before tuning front/rear balance.",
            [
                CreateEvidence("Average travel", FormatNumber(frontAverage, 1), "%", front.Name, ModeLabel(context.Request.TravelHistogramMode)),
                CreateEvidence("Average travel", FormatNumber(rearAverage, 1), "%", rear.Name, ModeLabel(context.Request.TravelHistogramMode)),
            ]));
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
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisCategory.Packing,
                SessionAnalysisSeverity.Action,
                CreateConfidence(context, side),
                $"{side.Name} rebound packing is plausible",
                $"The {side.Name.ToLowerInvariant()} is riding deep while rebound speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                "Try one or two clicks faster rebound and repeat the same section, then check whether average position and rider harshness improve.",
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true)));
            return;
        }

        if (deepAverage && repeatedBottomouts)
        {
            packingSides.Add(side.Type);
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisCategory.Packing,
                bottomoutSeverity!.Value,
                CreateConfidence(context, side),
                bottomoutSeverity == SessionAnalysisSeverity.Action
                    ? $"{side.Name} needs support before rebound diagnosis"
                    : $"{side.Name} support needs context before rebound diagnosis",
                $"The {side.Name.ToLowerInvariant()} is riding deep and has {side.Travel.Bottomouts} {BottomoutObservationName(context.Request.TravelHistogramMode)}, so support evidence should be separated from rebound packing before changing rebound.",
                bottomoutSeverity == SessionAnalysisSeverity.Action
                    ? "Address pressure or preload, progression, and compression support before opening rebound to chase a packing feel."
                    : "Repeat the same section or compare rider notes before treating these bottomouts as a chronic support problem.",
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true)));
            return;
        }

        if (shallowTravel && lowCompression)
        {
            packingSides.Add(side.Type);
            findings.Add(new SessionAnalysisFinding(
                SessionAnalysisCategory.Packing,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} may be resisting impacts",
                $"The {side.Name.ToLowerInvariant()} is not using much travel and compression speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                "Separate spring/progression from damping with a small compression-opening experiment, but avoid larger changes until travel support evidence agrees.",
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true)));
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
            findings.Add(new SessionAnalysisFinding(
                category,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} rebound is slow for profile context",
                $"The {side.Name.ToLowerInvariant()} rebound 95th percentile speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                "If the rider felt packing or poor recovery, try one click faster rebound and repeat the same section.",
                CreateDampingEvidence(side, context, includeTravel: false, includeDamperBands: true)));
        }
        else if (IsAboveFastReference(reboundP95, context.Profile.Rebound))
        {
            findings.Add(new SessionAnalysisFinding(
                category,
                SessionAnalysisSeverity.Watch,
                CreateConfidence(context, side),
                $"{side.Name} rebound is fast for profile context",
                $"The {side.Name.ToLowerInvariant()} rebound 95th percentile speed is well above the {ProfileLabel(context.Request.TargetProfile)} reference band.",
                "If the bike felt nervous or kicked back, try one click slower rebound and compare the same section.",
                CreateDampingEvidence(side, context, includeTravel: false, includeDamperBands: true)));
        }

        if (side.Travel.Bottomouts >= RepeatedBottomoutCount)
        {
            return;
        }

        if (IsBelowReference(compressionP95, context.Profile.Compression))
        {
            findings.Add(new SessionAnalysisFinding(
                category,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, side),
                $"{side.Name} compression speeds are subdued",
                $"The {side.Name.ToLowerInvariant()} compression 95th percentile speed is below the {ProfileLabel(context.Request.TargetProfile)} context band.",
                "Use rider feel and travel use before changing compression; this may also reflect smoother terrain or spring/progression.",
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true)));
        }
        else if (IsAboveFastReference(compressionP95, context.Profile.Compression))
        {
            findings.Add(new SessionAnalysisFinding(
                category,
                SessionAnalysisSeverity.Info,
                CreateConfidence(context, side),
                $"{side.Name} compression speeds are high",
                $"The {side.Name.ToLowerInvariant()} compression 95th percentile speed is above the {ProfileLabel(context.Request.TargetProfile)} context band.",
                "High speeds can simply mean hard terrain. Confirm support and bottomout margin before adding compression damping.",
                CreateDampingEvidence(side, context, includeTravel: true, includeDamperBands: true)));
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

        AddBalanceFinding(telemetryData, BalanceType.Rebound, ReboundBalanceSlopeWatchPercent, context, findings);
        AddBalanceFinding(telemetryData, BalanceType.Compression, CompressionBalanceSlopeWatchPercent, context, findings);
        if (HasAnyBalanceData(telemetryData, context) && HasLimitedBalanceContext(front, rear, context))
        {
            findings.Add(new SessionAnalysisFinding(
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
        AnalysisContext context,
        List<SessionAnalysisFinding> findings)
    {
        if (!telemetryData.HasBalanceData(balanceType, context.BalanceOptions))
        {
            return false;
        }

        var balance = telemetryData.CalculateBalance(balanceType, context.BalanceOptions);
        if (balance.AbsoluteSlopeDeltaPercent < thresholdPercent)
        {
            return false;
        }

        var frontMagnitude = Math.Abs(balance.FrontSlope);
        var rearMagnitude = Math.Abs(balance.RearSlope);
        var fasterSide = frontMagnitude >= rearMagnitude ? "front" : "rear";
        var slowerSide = frontMagnitude >= rearMagnitude ? "rear" : "front";
        var severity = balance.AbsoluteSlopeDeltaPercent >= thresholdPercent * 1.5
            ? SessionAnalysisSeverity.Action
            : SessionAnalysisSeverity.Watch;
        var typeLabel = balanceType == BalanceType.Rebound ? "rebound" : "compression";
        findings.Add(new SessionAnalysisFinding(
            SessionAnalysisCategory.Balance,
            severity,
            context.Request.AnalysisRange is null ? SessionAnalysisConfidence.Low : SessionAnalysisConfidence.Medium,
            $"{Capitalize(typeLabel)} balance slopes diverge",
            $"The {fasterSide} {typeLabel} trend is steeper than the {slowerSide} trend by {FormatPercent(balance.AbsoluteSlopeDeltaPercent)} using the selected {ModeLabel(context.Request.BalanceDisplacementMode).ToLowerInvariant()} balance mode.",
            "Use this as a front/rear experiment guide only after travel use looks reasonable; change the side whose travel and damping evidence agree with the imbalance.",
            [
                CreateEvidence("Slope delta", FormatNumber(balance.AbsoluteSlopeDeltaPercent, 1), "%", null, ModeLabel(context.Request.BalanceDisplacementMode)),
                CreateEvidence("Front slope", FormatNumber(balance.FrontSlope, 2), null, "Fork", ModeLabel(context.Request.BalanceDisplacementMode)),
                CreateEvidence("Rear slope", FormatNumber(balance.RearSlope, 2), null, "Rear", ModeLabel(context.Request.BalanceDisplacementMode)),
                CreateEvidence("Mean deviation", FormatNumber(balance.MeanSignedDeviation, 1), "mm/s", null, ModeLabel(context.Request.BalanceDisplacementMode)),
            ]));
        return true;
    }

    private static bool HasAnyBalanceData(TelemetryData telemetryData, AnalysisContext context)
    {
        return telemetryData.HasBalanceData(BalanceType.Rebound, context.BalanceOptions) ||
               telemetryData.HasBalanceData(BalanceType.Compression, context.BalanceOptions);
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
        if (front?.HasStrokeData == true && telemetryData.HasVibrationData(ImuLocation.Fork))
        {
            var vibration = telemetryData.CalculateVibration(ImuLocation.Fork, SuspensionType.Front, context.Request.AnalysisRange);
            if (vibration is not null)
            {
                addedComparableVibration = true;
                findings.Add(CreateVibrationContextFinding("Fork", "fork IMU", vibration));
            }
        }

        if (rear?.HasStrokeData == true && telemetryData.HasVibrationData(ImuLocation.Shock))
        {
            var vibration = telemetryData.CalculateVibration(ImuLocation.Shock, SuspensionType.Rear, context.Request.AnalysisRange);
            if (vibration is not null)
            {
                addedComparableVibration = true;
                findings.Add(CreateVibrationContextFinding("Rear", "shock IMU", vibration));
            }
        }

        if (!addedComparableVibration && telemetryData.ImuData is { Records.Count: > 0 })
        {
            findings.Add(new SessionAnalysisFinding(
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