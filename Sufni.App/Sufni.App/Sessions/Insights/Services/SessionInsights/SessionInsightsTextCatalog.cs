using System;
using System.Globalization;
using System.Linq;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Formatting;
namespace Sufni.App.Sessions.Insights.Services.SessionInsights;

/// <summary>
/// The per-finding user-facing text: titles, observations, fallback
/// recommendations, adjustment magnitude/effect phrases, and the evidence
/// label/format/source-mode mapping. Text moved verbatim from the previous
/// inline interpolations in <c>SessionInsightsService</c>.
/// </summary>
internal static class SessionInsightsTextCatalog
{
    public static string GetTitle(DiagnosticFinding finding, AnalysisContext context)
    {
        return finding.Id switch
        {
            SessionInsightsFindingId.FullSessionInsights => "Full session insights",
            SessionInsightsFindingId.ShortSelectedRange => "Short selected range",
            SessionInsightsFindingId.NoStrokeStatistics => "No stroke statistics",
            SessionInsightsFindingId.OneEndMissingStrokeData => "One end is missing stroke data",
            SessionInsightsFindingId.BalanceUnavailable => "Balance unavailable",
            SessionInsightsFindingId.LowStrokeCount => $"{FindingSideName(finding)} stroke count is low",
            SessionInsightsFindingId.ShallowTravelUse => $"{FindingSideName(finding)} travel use is shallow",
            SessionInsightsFindingId.RepeatedBottomouts => $"{FindingSideName(finding)} bottomed repeatedly",
            SessionInsightsFindingId.DeepTravelUse => $"{FindingSideName(finding)} is riding deep",
            SessionInsightsFindingId.DynamicSagMismatch => "Dynamic sag mismatch",
            SessionInsightsFindingId.ReboundPacking => $"{FindingSideName(finding)} rebound packing is plausible",
            SessionInsightsFindingId.SupportBeforeReboundDiagnosis => finding.Severity == SessionInsightsSeverity.Action
                ? $"{FindingSideName(finding)} needs support before rebound diagnosis"
                : $"{FindingSideName(finding)} support needs context before rebound diagnosis",
            SessionInsightsFindingId.ResistingImpacts => $"{FindingSideName(finding)} may be resisting impacts",
            SessionInsightsFindingId.ReboundSlowForProfileContext => $"{FindingSideName(finding)} rebound is slow for profile context",
            SessionInsightsFindingId.ReboundFastForProfileContext => $"{FindingSideName(finding)} rebound is fast for profile context",
            SessionInsightsFindingId.CompressionSpeedsSubdued => $"{FindingSideName(finding)} compression speeds are subdued",
            SessionInsightsFindingId.CompressionSpeedsHigh => $"{FindingSideName(finding)} compression speeds are high",
            SessionInsightsFindingId.BalanceContextLimited => "Balance context is limited",
            SessionInsightsFindingId.BalanceSlopesDiverge => $"{Capitalize(BalanceTypeLabel(finding))} balance slopes diverge",
            SessionInsightsFindingId.VibrationNotUsedForRecommendations => "Vibration not used for recommendations",
            SessionInsightsFindingId.VibrationContext => $"{FindingSideName(finding)} vibration context",
            _ => string.Empty,
        };
    }

    public static string GetObservation(DiagnosticFinding finding, AnalysisContext context)
    {
        return finding.Id switch
        {
            SessionInsightsFindingId.FullSessionInsights =>
                "The analysis is using the full session, so climbs, flats, stops, and transfers may be mixed into the suspension statistics.",
            SessionInsightsFindingId.ShortSelectedRange =>
                "The selected section is short, so a few events can dominate the statistics.",
            SessionInsightsFindingId.NoStrokeStatistics =>
                "The selected data does not contain usable compression or rebound strokes.",
            SessionInsightsFindingId.OneEndMissingStrokeData =>
                $"The {MissingStrokeDataSide(context)} side does not have usable strokes in this selection, so front/rear balance recommendations are skipped.",
            SessionInsightsFindingId.BalanceUnavailable =>
                "Balance needs enough front and rear compression or rebound events to fit trend lines.",
            SessionInsightsFindingId.LowStrokeCount =>
                $"Only {FormatCount(GetValue(finding, DiagnosticMeasurement.StrokeCount))} {FindingSideName(finding).ToLowerInvariant()} strokes are available in the selected data.",
            SessionInsightsFindingId.ShallowTravelUse =>
                $"The {FindingSideName(finding).ToLowerInvariant()} only reached {FormatPercent(GetValue(finding, DiagnosticMeasurement.MaxTravelPercent)!.Value)} of available travel in the selected {TravelModeDescription(context.Request.TravelDistributionMode)} data.",
            SessionInsightsFindingId.RepeatedBottomouts =>
                $"The selected data contains {FormatCount(GetValue(finding, DiagnosticMeasurement.Bottomouts))} {BottomoutObservationName(context.Request.TravelDistributionMode)} on the {FindingSideName(finding).ToLowerInvariant()} side.",
            SessionInsightsFindingId.DeepTravelUse =>
                $"The {FindingSideName(finding).ToLowerInvariant()} average position is {FormatPercent(GetValue(finding, DiagnosticMeasurement.AverageTravelPercent)!.Value)} in the selected {TravelModeDescription(context.Request.TravelDistributionMode)} data.",
            SessionInsightsFindingId.DynamicSagMismatch =>
                GetDynamicSagMismatchObservation(finding),
            SessionInsightsFindingId.ReboundPacking =>
                $"The {FindingSideName(finding).ToLowerInvariant()} is riding deep while rebound speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
            SessionInsightsFindingId.SupportBeforeReboundDiagnosis =>
                $"The {FindingSideName(finding).ToLowerInvariant()} is riding deep and has {FormatCount(GetValue(finding, DiagnosticMeasurement.Bottomouts))} {BottomoutObservationName(context.Request.TravelDistributionMode)}, so support evidence should be separated from rebound packing before changing rebound.",
            SessionInsightsFindingId.ResistingImpacts =>
                $"The {FindingSideName(finding).ToLowerInvariant()} is not using much travel and compression speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
            SessionInsightsFindingId.ReboundSlowForProfileContext =>
                $"The {FindingSideName(finding).ToLowerInvariant()} rebound 95th percentile speed is below the {ProfileLabel(context.Request.TargetProfile)} reference band.",
            SessionInsightsFindingId.ReboundFastForProfileContext =>
                $"The {FindingSideName(finding).ToLowerInvariant()} rebound 95th percentile speed is well above the {ProfileLabel(context.Request.TargetProfile)} reference band.",
            SessionInsightsFindingId.CompressionSpeedsSubdued =>
                $"The {FindingSideName(finding).ToLowerInvariant()} compression 95th percentile speed is below the {ProfileLabel(context.Request.TargetProfile)} context band.",
            SessionInsightsFindingId.CompressionSpeedsHigh =>
                $"The {FindingSideName(finding).ToLowerInvariant()} compression 95th percentile speed is above the {ProfileLabel(context.Request.TargetProfile)} context band.",
            SessionInsightsFindingId.BalanceContextLimited =>
                "The selected data has shallow travel use or low speed context, so any balance reading should be treated as supporting context rather than a tuning verdict.",
            SessionInsightsFindingId.BalanceSlopesDiverge =>
                GetBalanceSlopesDivergeObservation(finding, context),
            SessionInsightsFindingId.VibrationNotUsedForRecommendations =>
                "The available IMU data is missing a physically comparable travel pairing for this first-pass analysis.",
            SessionInsightsFindingId.VibrationContext =>
                $"The {FindingSideName(finding).ToLowerInvariant()} travel can be compared with the {VibrationImuLabel(finding)}; this is useful mainly against another run on the same section.",
            _ => string.Empty,
        };
    }

    public static string GetFallbackRecommendation(DiagnosticFinding finding, AnalysisContext context)
    {
        return finding.Id switch
        {
            SessionInsightsFindingId.FullSessionInsights =>
                "Select the difficult downhill section before making tuning changes from these findings.",
            SessionInsightsFindingId.ShortSelectedRange =>
                "Use a longer representative section if the trail allows, then compare the same segment after changes.",
            SessionInsightsFindingId.NoStrokeStatistics =>
                "Pick a section with clear suspension movement before interpreting damping or balance.",
            SessionInsightsFindingId.OneEndMissingStrokeData =>
                "Use a section where both ends of the bike are active if you want balance guidance.",
            SessionInsightsFindingId.BalanceUnavailable =>
                "Use a rougher or longer section before acting on front/rear balance.",
            SessionInsightsFindingId.LowStrokeCount =>
                "Treat damping recommendations as context until the same section has more usable events.",
            SessionInsightsFindingId.ShallowTravelUse =>
                "Check pressure or preload and progression before chasing fine damping; try a small change and rerun the same section.",
            SessionInsightsFindingId.RepeatedBottomouts => finding.Severity == SessionInsightsSeverity.Action
                ? "Review spring pressure or preload, end-stroke progression, and compression support before opening damping further."
                : "Treat this as a support clue and compare against rider notes before changing pressure, progression, or compression support.",
            SessionInsightsFindingId.DeepTravelUse =>
                "Confirm this was a hard descending section, then use packing and balance evidence before deciding between support and rebound changes.",
            SessionInsightsFindingId.DynamicSagMismatch =>
                "Treat this as a geometry choice first. If it was not intentional for the terrain, correct travel use before tuning front/rear balance.",
            SessionInsightsFindingId.ReboundPacking =>
                "Try one or two clicks faster rebound and repeat the same section, then check whether average position and rider harshness improve.",
            SessionInsightsFindingId.SupportBeforeReboundDiagnosis => finding.Severity == SessionInsightsSeverity.Action
                ? "Address pressure or preload, progression, and compression support before opening rebound to chase a packing feel."
                : "Repeat the same section or compare rider notes before treating these bottomouts as a chronic support problem.",
            SessionInsightsFindingId.ResistingImpacts =>
                "Separate spring/progression from damping with a small compression-opening experiment, but avoid larger changes until travel support evidence agrees.",
            SessionInsightsFindingId.ReboundSlowForProfileContext =>
                "If the rider felt packing or poor recovery, try one click faster rebound and repeat the same section.",
            SessionInsightsFindingId.ReboundFastForProfileContext =>
                "If the bike felt nervous or kicked back, try one click slower rebound and compare the same section.",
            SessionInsightsFindingId.CompressionSpeedsSubdued =>
                "Use rider feel and travel use before changing compression; this may also reflect smoother terrain or spring/progression.",
            SessionInsightsFindingId.CompressionSpeedsHigh =>
                "High speeds can simply mean hard terrain. Confirm support and bottomout margin before adding compression damping.",
            SessionInsightsFindingId.BalanceContextLimited =>
                "Resolve travel use and terrain context before treating balance findings as a dialed setup.",
            SessionInsightsFindingId.BalanceSlopesDiverge => finding.Adjustments.Count == 0
                ? "Resolve travel use / speed context before balance tuning."
                : "Use this as a front/rear experiment guide only after travel use looks reasonable; change the side whose travel and damping evidence agree with the imbalance.",
            SessionInsightsFindingId.VibrationNotUsedForRecommendations =>
                "Use vibration as context only until front travel is paired with fork IMU or rear travel is paired with shock IMU.",
            SessionInsightsFindingId.VibrationContext =>
                "Do not tune from vibration alone. Compare magic-carpet ratio, average g, travel use, and rider notes together.",
            _ => string.Empty,
        };
    }

    public static (string Magnitude, string ExpectedEffect) GetAdjustmentText(
        SessionInsightsFindingId findingId,
        DiagnosticAdjustment adjustment,
        AnalysisContext context)
    {
        return findingId switch
        {
            SessionInsightsFindingId.ShallowTravelUse => adjustment.Component switch
            {
                AdjustmentComponent.AirPressure =>
                    (SmallPressureMagnitude, "Max travel should rise toward 80-90 % on the same hard section."),
                _ =>
                    (OneClickMagnitude, "Max travel should rise toward 80-90 % on the same hard section."),
            },
            SessionInsightsFindingId.RepeatedBottomouts or SessionInsightsFindingId.SupportBeforeReboundDiagnosis =>
                adjustment.Component switch
                {
                    // The Tokens/Add effect differs between the Action variant
                    // (priority 1) and the Watch variant (priority 2).
                    AdjustmentComponent.Tokens when adjustment.Priority == 1 =>
                        ("1 token", "End-stroke ramp should resist deep travel; 100 % hits become rare."),
                    AdjustmentComponent.Tokens =>
                        ("1 token", "Repeated bottomouts should reduce; max settles near 90-95 %."),
                    _ =>
                        (SmallPressureMagnitude, "Repeated bottomouts should reduce; max settles near 90-95 %."),
                },
            SessionInsightsFindingId.DeepTravelUse => adjustment.Component switch
            {
                AdjustmentComponent.AirPressure =>
                    (SmallPressureMagnitude, "Average travel should ride 5-10 % shallower; rebound 95th should rise."),
                _ =>
                    (OneClickMagnitude, "Average travel should ride 5-10 % shallower; rebound 95th should rise."),
            },
            SessionInsightsFindingId.DynamicSagMismatch =>
                (SmallPressureMagnitude, "Front and rear average position should move closer together on the same steep section."),
            SessionInsightsFindingId.ReboundPacking => adjustment.Component switch
            {
                AdjustmentComponent.HighSpeedRebound =>
                    ("1-2 clicks", $"Rebound 95th should rise toward {FormatBand(context.Profile.Rebound)} mm/s and average position should ride shallower."),
                _ =>
                    (OneClickMagnitude, $"Rebound 95th should rise toward {FormatBand(context.Profile.Rebound)} mm/s and average position should ride shallower."),
            },
            SessionInsightsFindingId.ResistingImpacts => adjustment.Component switch
            {
                AdjustmentComponent.AirPressure =>
                    (SmallPressureMagnitude, "Compression 95th rises into reference and max travel reaches 80-90 %."),
                _ =>
                    ("1-2 clicks", "Compression 95th rises into reference and max travel reaches 80-90 %."),
            },
            SessionInsightsFindingId.ReboundSlowForProfileContext =>
                (OneClickMagnitude, $"Rebound 95th should rise into {FormatBand(context.Profile.Rebound)} mm/s."),
            SessionInsightsFindingId.ReboundFastForProfileContext =>
                (OneClickMagnitude, $"Rebound 95th should fall back into {FormatBand(context.Profile.Rebound)} mm/s; the bike should feel less nervous."),
            SessionInsightsFindingId.CompressionSpeedsSubdued =>
                (OneClickMagnitude, "Compression 95th should rise; max travel should rise with it."),
            SessionInsightsFindingId.CompressionSpeedsHigh =>
                (OneClickMagnitude, "Compression 95th should drop into reference; check that max travel still reaches 80-90 %."),
            SessionInsightsFindingId.BalanceSlopesDiverge => adjustment.Component switch
            {
                AdjustmentComponent.HighSpeedRebound or AdjustmentComponent.LowSpeedRebound =>
                    (OneClickMagnitude, "Rebound slope delta should drop under 20 % without making travel use worse."),
                _ =>
                    (OneClickMagnitude, "Compression slope delta should drop under 10 % without increasing bottomouts."),
            },
            _ => (string.Empty, string.Empty),
        };
    }

    public static (string Label, string Value, string? Unit, string SourceMode) DescribeEvidence(
        DiagnosticEvidence evidence,
        AnalysisContext context)
    {
        return evidence.Measurement switch
        {
            DiagnosticMeasurement.AnalysisRangeFullSession =>
                ("Analysis range", "Full session", null, "Selected range"),
            DiagnosticMeasurement.RangeDurationSeconds =>
                ("Range duration", FormatNumber(evidence.Value!.Value, 1), "s", "Selected range"),
            DiagnosticMeasurement.BalanceMode =>
                ("Balance mode", ModeLabel(context.Request.BalanceDisplacementMode), null, "Selected statistics mode"),
            DiagnosticMeasurement.StrokeCount =>
                ("Stroke count", FormatCount(evidence.Value), null, ModeLabel(context.Request.VelocityAverageMode)),
            DiagnosticMeasurement.MaxTravelPercent =>
                ("Max travel", FormatNullablePercent(evidence.Value), "%", ModeLabel(context.Request.TravelDistributionMode)),
            DiagnosticMeasurement.AverageTravelPercent =>
                ("Average travel", FormatNullablePercent(evidence.Value), "%", ModeLabel(context.Request.TravelDistributionMode)),
            DiagnosticMeasurement.Bottomouts =>
                (BottomoutEvidenceLabel(context.Request.TravelDistributionMode), FormatCount(evidence.Value), null, BottomoutSourceMode(context.Request.TravelDistributionMode)),
            DiagnosticMeasurement.HealthyHardSectionReference =>
                ("Healthy hard-section reference", FormatNumber(evidence.Value!.Value, 0), "%", "MotionIQ guide context"),
            DiagnosticMeasurement.CompressionP95 =>
                ("Compression p95", FormatSpeed(evidence.Value!.Value), "mm/s", ModeLabel(context.Request.VelocityAverageMode)),
            DiagnosticMeasurement.ReboundP95 =>
                ("Rebound p95", FormatSpeed(evidence.Value!.Value), "mm/s", ModeLabel(context.Request.VelocityAverageMode)),
            DiagnosticMeasurement.CompressionReferenceBand =>
                ("Compression reference", FormatBand(context.Profile.Compression), "mm/s", ProfileLabel(context.Request.TargetProfile)),
            DiagnosticMeasurement.ReboundReferenceBand =>
                ("Rebound reference", FormatBand(context.Profile.Rebound), "mm/s", ProfileLabel(context.Request.TargetProfile)),
            DiagnosticMeasurement.DampingBandHsc =>
                ("HSC", FormatNullablePercent(evidence.Value), "%", DampingBandSourceMode),
            DiagnosticMeasurement.DampingBandLsc =>
                ("LSC", FormatNullablePercent(evidence.Value), "%", DampingBandSourceMode),
            DiagnosticMeasurement.DampingBandHsr =>
                ("HSR", FormatNullablePercent(evidence.Value), "%", DampingBandSourceMode),
            DiagnosticMeasurement.DampingBandLsr =>
                ("LSR", FormatNullablePercent(evidence.Value), "%", DampingBandSourceMode),
            DiagnosticMeasurement.CompressionBalanceSlopeDeltaPercent or DiagnosticMeasurement.ReboundBalanceSlopeDeltaPercent =>
                ("Slope delta", FormatNumber(evidence.Value!.Value, 1), "%", ModeLabel(context.Request.BalanceDisplacementMode)),
            DiagnosticMeasurement.FrontBalanceSlope =>
                ("Front slope", FormatNumber(evidence.Value!.Value, 2), null, ModeLabel(context.Request.BalanceDisplacementMode)),
            DiagnosticMeasurement.RearBalanceSlope =>
                ("Rear slope", FormatNumber(evidence.Value!.Value, 2), null, ModeLabel(context.Request.BalanceDisplacementMode)),
            DiagnosticMeasurement.BalanceMeanSignedDeviation =>
                ("Mean deviation", FormatNumber(evidence.Value!.Value, 1), "mm/s", ModeLabel(context.Request.BalanceDisplacementMode)),
            DiagnosticMeasurement.BalanceContextLimit =>
                ("Context limit", "travel/speed", null, "Balance interpretation"),
            DiagnosticMeasurement.MagicCarpetRatio =>
                ("Magic carpet ratio", FormatNumber(evidence.Value!.Value, 2), null, ComparableVibrationSourceMode),
            DiagnosticMeasurement.AverageG =>
                ("Average g", FormatNumber(evidence.Value!.Value, 2), "g", ComparableVibrationSourceMode),
            DiagnosticMeasurement.CompressionVibrationPercent =>
                ("Compression vibration", FormatNumber(evidence.Value!.Value, 1), "%", ComparableVibrationSourceMode),
            DiagnosticMeasurement.ReboundVibrationPercent =>
                ("Rebound vibration", FormatNumber(evidence.Value!.Value, 1), "%", ComparableVibrationSourceMode),
            _ => (string.Empty, string.Empty, null, string.Empty),
        };
    }

    public static string SideName(SuspensionType side)
    {
        return side == SuspensionType.Front ? "Fork" : "Rear";
    }

    public static string TravelModeDescription(TravelDistributionMode mode)
    {
        return mode == TravelDistributionMode.DynamicSag ? "dynamic-sag" : "active-stroke";
    }

    public static string ModeLabel(TravelDistributionMode mode)
    {
        return mode == TravelDistributionMode.DynamicSag ? "Dynamic sag travel stats" : "Active suspension travel stats";
    }

    public static string ModeLabel(VelocityAverageMode mode)
    {
        return mode == VelocityAverageMode.StrokePeakAveraged ? "Stroke-peak average velocity" : "Sample-averaged velocity";
    }

    public static string ModeLabel(BalanceDisplacementMode mode)
    {
        return mode switch
        {
            BalanceDisplacementMode.Travel => "Travel balance",
            BalanceDisplacementMode.Speed => "Speed-position balance",
            _ => "Zenith balance",
        };
    }

    public static string ModeLabel(BalanceSpeedMode mode)
    {
        return mode switch
        {
            BalanceSpeedMode.LowSpeed => "low-speed",
            BalanceSpeedMode.HighSpeed => "high-speed",
            _ => "all-speed",
        };
    }

    public static string BottomoutEvidenceLabel(TravelDistributionMode mode)
    {
        return mode == TravelDistributionMode.DynamicSag ? "Bottomout windows" : "Stroke bottomouts";
    }

    public static string BottomoutSourceMode(TravelDistributionMode mode)
    {
        return mode == TravelDistributionMode.DynamicSag ? "Dynamic sag bottomout windows" : "Active suspension stroke bottomouts";
    }

    public static string BottomoutObservationName(TravelDistributionMode mode)
    {
        return mode == TravelDistributionMode.DynamicSag ? "bottomout windows" : "stroke bottomouts";
    }

    public static string ProfileLabel(SessionInsightsTargetProfile profile)
    {
        return profile switch
        {
            SessionInsightsTargetProfile.DH => "DH profile",
            _ => $"{profile} profile",
        };
    }

    public static string FormatBand(SpeedBand band)
    {
        return $"{FormatSpeed(band.Low)}-{FormatSpeed(band.High)}";
    }

    // Analysis evidence has always been formatted invariantly; keep it that
    // way while display surfaces use the formatter's current-culture default.
    public static string FormatSpeed(double value)
    {
        return UnitsFormatter.FormatSpeed(value, CultureInfo.InvariantCulture);
    }

    public static string FormatNumber(double value, int decimals)
    {
        return UnitsFormatter.FormatNumber(value, decimals, CultureInfo.InvariantCulture);
    }

    public static string FormatPercent(double value)
    {
        return $"{FormatNumber(value, 1)}%";
    }

    public static string FormatNullablePercent(double? value)
    {
        return value is null ? "n/a" : FormatNumber(value.Value, 1);
    }

    public static string Capitalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private const string SmallPressureMagnitude = "small (~2-5 PSI)";
    private const string OneClickMagnitude = "1 click";
    private const string DampingBandSourceMode = "Current damping band percentages";
    private const string ComparableVibrationSourceMode = "Comparable vibration";

    private static string FindingSideName(DiagnosticFinding finding)
    {
        var side = finding.Evidence.FirstOrDefault(evidence => evidence.Side is not null)?.Side;
        return side is { } value ? SideName(value) : string.Empty;
    }

    private static string FormatCount(double? value)
    {
        return value is null ? string.Empty : ((int)value.Value).ToString(CultureInfo.InvariantCulture);
    }

    private static double? GetValue(DiagnosticFinding finding, DiagnosticMeasurement measurement)
    {
        return finding.Evidence.FirstOrDefault(evidence => evidence.Measurement == measurement)?.Value;
    }

    private static string MissingStrokeDataSide(AnalysisContext context)
    {
        var frontHasStrokeData = TelemetryStatistics.HasStrokeData(
            context.Request.TelemetryData!,
            SuspensionType.Front,
            context.Request.AnalysisRange);
        return frontHasStrokeData ? "rear" : "front";
    }

    private static string GetDynamicSagMismatchObservation(DiagnosticFinding finding)
    {
        var frontAverage = finding.Evidence
            .First(evidence => evidence.Measurement == DiagnosticMeasurement.AverageTravelPercent && evidence.Side == SuspensionType.Front)
            .Value!.Value;
        var rearAverage = finding.Evidence
            .First(evidence => evidence.Measurement == DiagnosticMeasurement.AverageTravelPercent && evidence.Side == SuspensionType.Rear)
            .Value!.Value;
        var delta = frontAverage - rearAverage;
        var higherSide = delta > 0 ? SideName(SuspensionType.Front) : SideName(SuspensionType.Rear);
        var lowerSide = delta > 0 ? SideName(SuspensionType.Rear) : SideName(SuspensionType.Front);
        return $"The {higherSide.ToLowerInvariant()} is averaging deeper than the {lowerSide.ToLowerInvariant()} by {FormatPercent(Math.Abs(delta))} in the selected travel mode.";
    }

    private static string GetBalanceSlopesDivergeObservation(DiagnosticFinding finding, AnalysisContext context)
    {
        var delta = (GetValue(finding, DiagnosticMeasurement.ReboundBalanceSlopeDeltaPercent)
                     ?? GetValue(finding, DiagnosticMeasurement.CompressionBalanceSlopeDeltaPercent))!.Value;
        var frontMagnitude = Math.Abs(GetValue(finding, DiagnosticMeasurement.FrontBalanceSlope)!.Value);
        var rearMagnitude = Math.Abs(GetValue(finding, DiagnosticMeasurement.RearBalanceSlope)!.Value);
        var fasterSide = frontMagnitude >= rearMagnitude ? "front" : "rear";
        var slowerSide = frontMagnitude >= rearMagnitude ? "rear" : "front";
        var typeLabel = BalanceTypeLabel(finding);
        return $"The {fasterSide} {typeLabel} trend is steeper than the {slowerSide} trend by {FormatPercent(delta)} using the selected {ModeLabel(context.Request.BalanceDisplacementMode).ToLowerInvariant()} balance mode.";
    }

    private static string BalanceTypeLabel(DiagnosticFinding finding)
    {
        return finding.Evidence.Any(evidence => evidence.Measurement == DiagnosticMeasurement.ReboundBalanceSlopeDeltaPercent)
            ? "rebound"
            : "compression";
    }

    private static string VibrationImuLabel(DiagnosticFinding finding)
    {
        var side = finding.Evidence.FirstOrDefault(evidence => evidence.Side is not null)?.Side;
        return side == SuspensionType.Front ? "fork IMU" : "shock IMU";
    }
}
