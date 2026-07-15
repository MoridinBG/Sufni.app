using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Presentation;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Insights.Services.SessionInsights;

/// <summary>
/// Materializes the presentation contract (steps, metrics, display findings,
/// vibration panel) from a typed <see cref="SessionDiagnosticsReport"/>. Only
/// this layer references <see cref="SurfacePresentationState"/> and the
/// display records.
/// </summary>
internal static class SessionInsightsPresenter
{
    public static SessionInsightsResult Present(SessionDiagnosticsReport report, AnalysisContext context)
    {
        var telemetryData = context.Request.TelemetryData!;
        var presented = report.Findings
            .Select(finding => new PresentedFinding(finding, PresentFinding(finding, context)))
            .ToArray();
        var displayFindings = presented.Select(pair => pair.Display).ToArray();

        return new SessionInsightsResult(
            SurfacePresentationState.Ready,
            BuildSteps(telemetryData, report.Front, report.Rear, context, presented),
            displayFindings.Where(finding => finding.Category == SessionInsightsCategory.DataQuality).ToArray(),
            BuildVibrationPanel(report.FrontVibration, report.RearVibration),
            displayFindings)
        {
            NextStep = BuildNextStep(presented),
        };
    }

    private const string SagTitle = "Sag & travel use";
    private const string ForkTitle = "Fork";
    private const string RearTitle = "Rear";
    private const string BalanceTitle = "Balance";

    private sealed record PresentedFinding(DiagnosticFinding Typed, SessionInsightsFinding Display);

    private static SessionInsightsFinding PresentFinding(DiagnosticFinding finding, AnalysisContext context)
    {
        var adjustments = finding.Adjustments
            .Select(adjustment => CreateAdjustment(finding.Id, adjustment, context))
            .ToArray();
        var evidence = finding.Evidence
            .Select(item => CreateEvidence(item, context))
            .ToArray();

        return new SessionInsightsFinding(
            finding.Id,
            finding.Category,
            finding.Severity,
            finding.Confidence,
            SessionInsightsTextCatalog.GetTitle(finding, context),
            SessionInsightsTextCatalog.GetObservation(finding, context),
            BuildRecommendationText(adjustments, SessionInsightsTextCatalog.GetFallbackRecommendation(finding, context)),
            evidence,
            adjustments);
    }

    private static Adjustment CreateAdjustment(
        SessionInsightsFindingId findingId,
        DiagnosticAdjustment adjustment,
        AnalysisContext context)
    {
        var (magnitude, expectedEffect) = SessionInsightsTextCatalog.GetAdjustmentText(findingId, adjustment, context);
        return new Adjustment(
            adjustment.Component,
            adjustment.Direction,
            magnitude,
            SessionInsightsTextCatalog.SideName(adjustment.Side),
            expectedEffect,
            adjustment.Priority);
    }

    private static SessionInsightsEvidence CreateEvidence(DiagnosticEvidence evidence, AnalysisContext context)
    {
        var (label, value, unit, sourceMode) = SessionInsightsTextCatalog.DescribeEvidence(evidence, context);
        var side = evidence.Side is { } sideValue ? SessionInsightsTextCatalog.SideName(sideValue) : null;
        return new SessionInsightsEvidence(label, value, unit, side, sourceMode);
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

    private static IReadOnlyList<SessionInsightsStep> BuildSteps(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        IReadOnlyList<PresentedFinding> findings)
    {
        var steps = new[]
        {
            BuildStep(
                SessionInsightsStepId.Sag,
                SagTitle,
                BuildSagMetrics(front, rear),
                FilterFindings(findings, IsSagFinding)),
            BuildStep(
                SessionInsightsStepId.Fork,
                ForkTitle,
                BuildSideMetrics(front, context),
                FilterFindings(findings, IsForkFinding)),
            BuildStep(
                SessionInsightsStepId.Rear,
                RearTitle,
                BuildSideMetrics(rear, context),
                FilterFindings(findings, IsRearFinding)),
            BuildStep(
                SessionInsightsStepId.Balance,
                BalanceTitle,
                BuildBalanceMetrics(telemetryData, context),
                FilterFindings(findings, IsBalanceStepFinding)),
        };

        return ApplyGating(steps);
    }

    // The guide tunes in order (sag -> fork -> rear -> balance); flag any later
    // step that still shows an issue while an earlier one is unresolved, since
    // fixing the earlier area usually shifts the later numbers.
    private static IReadOnlyList<SessionInsightsStep> ApplyGating(IReadOnlyList<SessionInsightsStep> steps)
    {
        var gated = new List<SessionInsightsStep>(steps.Count);
        string? earliestUnresolved = null;
        foreach (var step in steps)
        {
            gated.Add(step.HasIssue && earliestUnresolved is not null
                ? step with { GatingMessage = $"Sort out “{earliestUnresolved}” first; its changes usually shift these numbers." }
                : step);

            if (step.HasIssue && earliestUnresolved is null)
            {
                earliestUnresolved = step.Title;
            }
        }

        return gated;
    }

    // The single most important experiment across the whole tab: highest
    // severity, then earliest step in the tuning order, then confidence, then
    // the finding's own adjustment priority.
    private static SessionInsightsNextStep? BuildNextStep(IReadOnlyList<PresentedFinding> findings)
    {
        SessionInsightsNextStep? best = null;
        (SessionInsightsSeverity Severity, int Order, SessionInsightsConfidence Confidence, int Priority)? bestKey = null;

        foreach (var pair in findings)
        {
            if (ResolveStepForFinding(pair.Typed) is not { } step)
            {
                continue;
            }

            foreach (var adjustment in pair.Display.Adjustments)
            {
                var key = (pair.Display.Severity, step.Order, pair.Display.Confidence, adjustment.Priority);
                if (bestKey is null || IsBetterNextStep(key, bestKey.Value))
                {
                    bestKey = key;
                    best = new SessionInsightsNextStep(step.Title, adjustment, pair.Display.Observation);
                }
            }
        }

        return best;
    }

    private static bool IsBetterNextStep(
        (SessionInsightsSeverity Severity, int Order, SessionInsightsConfidence Confidence, int Priority) candidate,
        (SessionInsightsSeverity Severity, int Order, SessionInsightsConfidence Confidence, int Priority) current)
    {
        if (candidate.Severity != current.Severity)
        {
            return candidate.Severity > current.Severity;
        }

        if (candidate.Order != current.Order)
        {
            return candidate.Order < current.Order;
        }

        if (candidate.Confidence != current.Confidence)
        {
            return candidate.Confidence > current.Confidence;
        }

        return candidate.Priority < current.Priority;
    }

    private static (int Order, string Title)? ResolveStepForFinding(DiagnosticFinding finding)
    {
        if (IsSagFinding(finding))
        {
            return (1, SagTitle);
        }

        if (IsForkFinding(finding))
        {
            return (2, ForkTitle);
        }

        if (IsRearFinding(finding))
        {
            return (3, RearTitle);
        }

        if (IsBalanceStepFinding(finding))
        {
            return (4, BalanceTitle);
        }

        return null;
    }

    private static IReadOnlyList<SessionInsightsFinding> FilterFindings(
        IReadOnlyList<PresentedFinding> findings,
        Func<DiagnosticFinding, bool> predicate)
    {
        return findings
            .Where(pair => predicate(pair.Typed))
            .Select(pair => pair.Display)
            .ToArray();
    }

    private static SessionInsightsStep BuildStep(
        SessionInsightsStepId id,
        string title,
        IReadOnlyList<SessionInsightsMetric> metrics,
        IReadOnlyList<SessionInsightsFinding> findings)
    {
        var hasIssue = findings.Any(finding => finding.Severity != SessionInsightsSeverity.Info);
        var verdict = hasIssue
            ? findings.Max(finding => finding.Severity)
            : SessionInsightsSeverity.Info;

        return new SessionInsightsStep(id, title, verdict, hasIssue, metrics, findings);
    }

    private static bool IsSagFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionInsightsCategory.TravelUse ||
               (finding.Category == SessionInsightsCategory.Balance &&
                finding.Id == SessionInsightsFindingId.DynamicSagMismatch);
    }

    private static bool IsForkFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionInsightsCategory.ForkDamping ||
               (finding.Category == SessionInsightsCategory.Packing && FindingSide(finding) == SuspensionType.Front);
    }

    private static bool IsRearFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionInsightsCategory.RearDamping ||
               (finding.Category == SessionInsightsCategory.Packing && FindingSide(finding) == SuspensionType.Rear);
    }

    private static bool IsBalanceStepFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionInsightsCategory.Balance &&
               finding.Id != SessionInsightsFindingId.DynamicSagMismatch;
    }

    private static SuspensionType? FindingSide(DiagnosticFinding finding)
    {
        return finding.Evidence.FirstOrDefault(evidence => evidence.Side is not null)?.Side;
    }

    private static IReadOnlyList<SessionInsightsMetric> BuildSagMetrics(
        SideSnapshot? front,
        SideSnapshot? rear)
    {
        var metrics = new List<SessionInsightsMetric>
        {
            CreateMetric("Fork max", FormatNullablePercent(front?.MaxTravelPercent), "%", "Fork", ">= 85 % on hard terrain", TravelUseStatus(front?.MaxTravelPercent), MaxTravelTooltip),
            CreateMetric("Fork avg", FormatNullablePercent(front?.AverageTravelPercent), "%", "Fork", "compare against baseline", tooltip: AverageTravelTooltip),
            CreateMetric("Rear max", FormatNullablePercent(rear?.MaxTravelPercent), "%", "Rear", ">= 85 % on hard terrain", TravelUseStatus(rear?.MaxTravelPercent), MaxTravelTooltip),
            CreateMetric("Rear avg", FormatNullablePercent(rear?.AverageTravelPercent), "%", "Rear", "compare against baseline", tooltip: AverageTravelTooltip),
        };

        if (front?.AverageTravelPercent is { } frontAverage && rear?.AverageTravelPercent is { } rearAverage)
        {
            var delta = Math.Abs(frontAverage - rearAverage);
            metrics.Add(CreateMetric(
                "Avg delta",
                FormatNumber(delta, 1),
                "%",
                null,
                "front/rear average within ~10-15 %",
                WithinThresholdStatus(delta, SessionDiagnostics.DynamicSagMismatchWatchPercent),
                AvgDeltaTooltip));
        }

        return metrics;
    }

    private static IReadOnlyList<SessionInsightsMetric> BuildSideMetrics(
        SideSnapshot? side,
        AnalysisContext context)
    {
        if (side is null)
        {
            return
            [
                CreateMetric("Comp 95th", "n/a", "mm/s", null, FormatBand(context.Profile.Compression), tooltip: Percentile95Tooltip),
                CreateMetric("Reb 95th", "n/a", "mm/s", null, FormatBand(context.Profile.Rebound), tooltip: Percentile95Tooltip),
                CreateMetric("Max travel", "n/a", "%", null, ">= 85 % on hard terrain", tooltip: MaxTravelTooltip),
            ];
        }

        var sideName = SessionInsightsTextCatalog.SideName(side.Type);
        var compressionP95 = Math.Abs(side.Velocity.Percentile95Compression);
        var reboundP95 = Math.Abs(side.Velocity.Percentile95Rebound);
        return
        [
            CreateMetric("Comp 95th", FormatSpeed(compressionP95), "mm/s", sideName, FormatBand(context.Profile.Compression), SpeedBandStatus(compressionP95, context.Profile.Compression), Percentile95Tooltip),
            CreateMetric("Reb 95th", FormatSpeed(reboundP95), "mm/s", sideName, FormatBand(context.Profile.Rebound), SpeedBandStatus(reboundP95, context.Profile.Rebound), Percentile95Tooltip),
            CreateMetric("Max travel", FormatNullablePercent(side.MaxTravelPercent), "%", sideName, ">= 85 % on hard terrain", TravelUseStatus(side.MaxTravelPercent), MaxTravelTooltip),
        ];
    }

    private static IReadOnlyList<SessionInsightsMetric> BuildBalanceMetrics(
        TelemetryData telemetryData,
        AnalysisContext context)
    {
        var compression = BalanceDelta(telemetryData, BalanceType.Compression, context, SessionDiagnostics.CompressionBalanceSlopeWatchPercent);
        var rebound = BalanceDelta(telemetryData, BalanceType.Rebound, context, SessionDiagnostics.ReboundBalanceSlopeWatchPercent);
        return
        [
            CreateMetric("Compression slope delta", compression.Display, "%", null, "< 10 %", compression.Status, SlopeDeltaTooltip),
            CreateMetric("Rebound slope delta", rebound.Display, "%", null, "< 20 %", rebound.Status, SlopeDeltaTooltip),
        ];
    }

    private static (string Display, SessionInsightsMetricStatus Status) BalanceDelta(
        TelemetryData telemetryData,
        BalanceType balanceType,
        AnalysisContext context,
        double watchThresholdPercent)
    {
        if (!TelemetryStatistics.HasBalanceData(telemetryData, balanceType, context.BalanceOptions))
        {
            return ("n/a", SessionInsightsMetricStatus.Neutral);
        }

        var balance = TelemetryStatistics.CalculateBalance(telemetryData, balanceType, context.BalanceOptions);
        var delta = balance.AbsoluteSlopeDeltaPercent;
        return (FormatNumber(delta, 1), WithinThresholdStatus(delta, watchThresholdPercent));
    }

    private static SessionInsightsVibrationPanel? BuildVibrationPanel(
        VibrationStats? frontVibration,
        VibrationStats? rearVibration)
    {
        var metrics = new List<SessionInsightsMetric>();
        AddVibrationMetrics(metrics, frontVibration, SuspensionType.Front);
        AddVibrationMetrics(metrics, rearVibration, SuspensionType.Rear);

        return metrics.Count == 0
            ? null
            : new SessionInsightsVibrationPanel(
                metrics,
                "Compare vibration only side-by-side on the same trail at the same pace.");
    }

    private static void AddVibrationMetrics(
        List<SessionInsightsMetric> metrics,
        VibrationStats? vibration,
        SuspensionType suspensionType)
    {
        if (vibration is null)
        {
            return;
        }

        var sideName = SessionInsightsTextCatalog.SideName(suspensionType);
        metrics.Add(CreateMetric("Magic carpet ratio", FormatNumber(vibration.MagicCarpet, 2), null, sideName, null, tooltip: MagicCarpetTooltip));
        metrics.Add(CreateMetric("Average g", FormatNumber(vibration.AverageGOverall, 2), "g", sideName, null));
        metrics.Add(CreateMetric("Compression vibration", FormatNumber(vibration.CompressionPercent, 1), "%", sideName, null));
        metrics.Add(CreateMetric("Rebound vibration", FormatNumber(vibration.ReboundPercent, 1), "%", sideName, null));
    }

    private static SessionInsightsMetric CreateMetric(
        string label,
        string value,
        string? unit,
        string? side,
        string? targetRange,
        SessionInsightsMetricStatus status = SessionInsightsMetricStatus.Neutral,
        string? tooltip = null)
    {
        return new SessionInsightsMetric(label, value, unit, side, targetRange, status, tooltip);
    }

    // Travel-use metrics are healthy at or above the guide's hard-section
    // reference (~85 %); below it reads as under-using travel.
    private static SessionInsightsMetricStatus TravelUseStatus(double? percent)
    {
        if (percent is null)
        {
            return SessionInsightsMetricStatus.Neutral;
        }

        return percent.Value >= SessionDiagnostics.HealthyHardSegmentTravelPercent
            ? SessionInsightsMetricStatus.Good
            : SessionInsightsMetricStatus.BelowTarget;
    }

    // Speed metrics mirror the diagnostics: below the band reads slow, above the
    // fast reference reads high, anything in between is on target.
    private static SessionInsightsMetricStatus SpeedBandStatus(double value, SpeedBand band)
    {
        if (value <= 0)
        {
            return SessionInsightsMetricStatus.Neutral;
        }

        if (value < band.Low)
        {
            return SessionInsightsMetricStatus.BelowTarget;
        }

        return value > band.High * SessionDiagnostics.VeryFastMultiplier
            ? SessionInsightsMetricStatus.AboveTarget
            : SessionInsightsMetricStatus.Good;
    }

    // "Keep it under X" metrics (slope deltas, sag delta): on target below the
    // threshold, drawing attention at or above it.
    private static SessionInsightsMetricStatus WithinThresholdStatus(double value, double threshold)
    {
        return value < threshold
            ? SessionInsightsMetricStatus.Good
            : SessionInsightsMetricStatus.AboveTarget;
    }

    private const string Percentile95Tooltip =
        "95th-percentile stroke speed: only the fastest 5% of strokes beat it. A stable stand-in for peak speed that ignores one-off spikes.";

    private const string MaxTravelTooltip =
        "The deepest the suspension went, as a share of available travel. Aim for around 85% on hard terrain.";

    private const string AverageTravelTooltip =
        "Dynamic sag: average ride height over the selected data. Compare front against rear and against your own baseline.";

    private const string AvgDeltaTooltip =
        "How far apart front and rear average ride height sit. A big gap means the bike is pitched forward or back.";

    private const string SlopeDeltaTooltip =
        "Balance slope delta: how differently the front and rear speed-vs-travel trend lines are tilted. Smaller means the ends move more alike.";

    private const string MagicCarpetTooltip =
        "Magic-carpet ratio: suspension movement per unit of vibration reaching the rider. Higher is smoother. Only compare against another run on the same trail.";

    private static string FormatBand(SpeedBand band) => SessionInsightsTextCatalog.FormatBand(band);

    private static string FormatSpeed(double value) => SessionInsightsTextCatalog.FormatSpeed(value);

    private static string FormatNumber(double value, int decimals) => SessionInsightsTextCatalog.FormatNumber(value, decimals);

    private static string FormatNullablePercent(double? value) => SessionInsightsTextCatalog.FormatNullablePercent(value);
}
