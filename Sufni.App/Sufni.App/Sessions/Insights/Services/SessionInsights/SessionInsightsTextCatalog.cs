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
            SessionInsightsFindingId.LowStrokeCount => $"Not many {FindingSideName(finding).ToLowerInvariant()} strokes yet",
            SessionInsightsFindingId.ShallowTravelUse => $"{FindingSideName(finding)} isn't using its travel",
            SessionInsightsFindingId.RepeatedBottomouts => $"{FindingSideName(finding)} is bottoming out",
            SessionInsightsFindingId.DeepTravelUse => $"{FindingSideName(finding)} rides deep",
            SessionInsightsFindingId.DynamicSagMismatch => "Front and rear sit at different heights",
            SessionInsightsFindingId.ReboundPacking => $"{FindingSideName(finding)} may be packing up",
            SessionInsightsFindingId.SupportBeforeReboundDiagnosis => finding.Severity == SessionInsightsSeverity.Action
                ? $"{FindingSideName(finding)} needs more support"
                : $"{FindingSideName(finding)} support needs a closer look",
            SessionInsightsFindingId.ResistingImpacts => $"{FindingSideName(finding)} is resisting impacts",
            SessionInsightsFindingId.ReboundSlowForProfileContext => $"{FindingSideName(finding)} rebound is slow",
            SessionInsightsFindingId.ReboundFastForProfileContext => $"{FindingSideName(finding)} rebound is fast",
            SessionInsightsFindingId.CompressionSpeedsSubdued => $"{FindingSideName(finding)} is firm over impacts",
            SessionInsightsFindingId.CompressionSpeedsHigh => $"{FindingSideName(finding)} moves fast over impacts",
            SessionInsightsFindingId.BalanceContextLimited => "Balance reading is only rough here",
            SessionInsightsFindingId.BalanceSlopesDiverge => $"{Capitalize(BalanceTypeLabel(finding))} balance is off",
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
                $"The {FindingSideName(finding).ToLowerInvariant()} only reached {FormatPercent(GetValue(finding, DiagnosticMeasurement.MaxTravelPercent)!.Value)} of its travel on the hardest part of the run.",
            SessionInsightsFindingId.RepeatedBottomouts =>
                $"The {FindingSideName(finding).ToLowerInvariant()} hit the bottom of its travel {FormatCount(GetValue(finding, DiagnosticMeasurement.Bottomouts))} times.",
            SessionInsightsFindingId.DeepTravelUse =>
                $"The {FindingSideName(finding).ToLowerInvariant()} is sitting deep in its travel ({FormatPercent(GetValue(finding, DiagnosticMeasurement.AverageTravelPercent)!.Value)} on average).",
            SessionInsightsFindingId.DynamicSagMismatch =>
                GetDynamicSagMismatchObservation(finding),
            SessionInsightsFindingId.ReboundPacking =>
                $"The {FindingSideName(finding).ToLowerInvariant()} is riding deep and springing back slowly, so it may not recover between hits.",
            SessionInsightsFindingId.SupportBeforeReboundDiagnosis =>
                $"The {FindingSideName(finding).ToLowerInvariant()} is riding deep and bottoming out, so sort out support before touching rebound.",
            SessionInsightsFindingId.ResistingImpacts =>
                $"The {FindingSideName(finding).ToLowerInvariant()} isn't using much travel and isn't moving quickly over impacts.",
            SessionInsightsFindingId.ReboundSlowForProfileContext =>
                $"The {FindingSideName(finding).ToLowerInvariant()} springs back slower than {ProfileRidingName(context.Request.TargetProfile)} riding usually wants.",
            SessionInsightsFindingId.ReboundFastForProfileContext =>
                $"The {FindingSideName(finding).ToLowerInvariant()} springs back faster than {ProfileRidingName(context.Request.TargetProfile)} riding usually wants.",
            SessionInsightsFindingId.CompressionSpeedsSubdued =>
                $"The {FindingSideName(finding).ToLowerInvariant()} isn't moving over impacts as quickly as {ProfileRidingName(context.Request.TargetProfile)} riding usually needs.",
            SessionInsightsFindingId.CompressionSpeedsHigh =>
                $"The {FindingSideName(finding).ToLowerInvariant()} is moving very quickly over impacts for {ProfileRidingName(context.Request.TargetProfile)} riding.",
            SessionInsightsFindingId.BalanceContextLimited =>
                "Travel use is shallow or speeds are low here, so treat the balance reading as a rough hint, not a verdict.",
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
        // Expected effects are directional and iterative on purpose: we do not
        // know how much any one click moves a given damper, so they describe the
        // trend to look for and tell the rider to re-check, not a target to hit.
        return findingId switch
        {
            SessionInsightsFindingId.ShallowTravelUse => adjustment.Component switch
            {
                AdjustmentComponent.AirPressure =>
                    (SmallPressureMagnitude, "It should start using more of its travel. Re-check the same section and adjust again if needed."),
                _ =>
                    (OneClickMagnitude, "It should start using more of its travel. Re-check the same section and adjust again if needed."),
            },
            SessionInsightsFindingId.RepeatedBottomouts or SessionInsightsFindingId.SupportBeforeReboundDiagnosis =>
                adjustment.Component switch
                {
                    AdjustmentComponent.Tokens =>
                        ("1 token", "Hard hits should stop slamming through. Add one, then re-check."),
                    _ =>
                        (SmallPressureMagnitude, "Bottom-outs should ease off. Change a little at a time and re-check."),
                },
            SessionInsightsFindingId.DeepTravelUse => adjustment.Component switch
            {
                AdjustmentComponent.AirPressure =>
                    (SmallPressureMagnitude, "It should sit a little higher and recover a bit sooner. Adjust gradually and re-check."),
                _ =>
                    (OneClickMagnitude, "It should sit a little higher and recover a bit sooner. Adjust gradually and re-check."),
            },
            SessionInsightsFindingId.DynamicSagMismatch =>
                (SmallPressureMagnitude, "Front and rear should sit closer to the same height. Adjust a little and re-check."),
            SessionInsightsFindingId.ReboundPacking =>
                (OneClickMagnitude, "It should start recovering between hits. Open gradually and re-check — it may take a few clicks."),
            SessionInsightsFindingId.ResistingImpacts => adjustment.Component switch
            {
                AdjustmentComponent.AirPressure =>
                    (SmallPressureMagnitude, "It should move more freely over impacts. Change a little at a time and re-check."),
                _ =>
                    (OneClickMagnitude, "It should move more freely over impacts. Change a little at a time and re-check."),
            },
            SessionInsightsFindingId.ReboundSlowForProfileContext =>
                (OneClickMagnitude, "The wheel should return to the ground a bit sooner. Open gradually and re-check — it may take more than one click."),
            SessionInsightsFindingId.ReboundFastForProfileContext =>
                (OneClickMagnitude, "The bike should feel a bit calmer. Close gradually and re-check."),
            SessionInsightsFindingId.CompressionSpeedsSubdued =>
                (OneClickMagnitude, "It should react a bit more to impacts. Open gradually and re-check."),
            SessionInsightsFindingId.CompressionSpeedsHigh =>
                (OneClickMagnitude, "Big impacts should feel a bit more controlled. Close gradually and re-check, keeping an eye on travel use."),
            SessionInsightsFindingId.BalanceSlopesDiverge =>
                (OneClickMagnitude, "Front and rear should start moving more alike. Adjust gradually and re-check."),
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

    public static string ProfileLabel(SessionInsightsTargetProfile profile)
    {
        return profile switch
        {
            SessionInsightsTargetProfile.DH => "DH profile",
            _ => $"{profile} profile",
        };
    }

    // Plain riding-style word for inline sentences ("enduro riding"), as opposed
    // to the evidence source-mode label ("Enduro profile").
    public static string ProfileRidingName(SessionInsightsTargetProfile profile)
    {
        return profile == SessionInsightsTargetProfile.DH
            ? "DH"
            : profile.ToString().ToLowerInvariant();
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

    // Magnitudes are framed as small starting steps, not prescriptions: the
    // per-click effect on any given damper is unknown, so the guidance is to
    // change gradually and re-check.
    private const string SmallPressureMagnitude = "a little (~2-5 PSI)";
    private const string OneClickMagnitude = "a click at a time";
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
        return $"The {higherSide.ToLowerInvariant()} is sitting about {FormatPercent(Math.Abs(delta))} deeper than the {lowerSide.ToLowerInvariant()}.";
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
        return $"The {fasterSide} and {slowerSide} aren't moving alike on {typeLabel} — about {FormatPercent(delta)} apart.";
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
