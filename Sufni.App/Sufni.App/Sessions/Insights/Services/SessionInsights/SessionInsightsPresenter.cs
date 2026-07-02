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
            BuildVibrationPanel(telemetryData, report.Front, report.Rear, context),
            displayFindings);
    }

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
        return
        [
            BuildStep(
                SessionInsightsStepId.Sag,
                "Sag & travel use",
                BuildSagMetrics(front, rear),
                FilterFindings(findings, IsSagFinding)),
            BuildStep(
                SessionInsightsStepId.Fork,
                "Fork",
                BuildSideMetrics(front, context),
                FilterFindings(findings, IsForkFinding)),
            BuildStep(
                SessionInsightsStepId.Rear,
                "Rear",
                BuildSideMetrics(rear, context),
                FilterFindings(findings, IsRearFinding)),
            BuildStep(
                SessionInsightsStepId.Balance,
                "Balance",
                BuildBalanceMetrics(telemetryData, context),
                FilterFindings(findings, IsBalanceStepFinding)),
        ];
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

        return new SessionInsightsStep(id, title, verdict, hasIssue, metrics, primary, alternates, findings);
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

    private static bool IsSameAdjustment(Adjustment left, Adjustment right)
    {
        return left.Component == right.Component &&
               left.Direction == right.Direction &&
               left.Side == right.Side;
    }

    private static IReadOnlyList<SessionInsightsMetric> BuildSagMetrics(
        SideSnapshot? front,
        SideSnapshot? rear)
    {
        var metrics = new List<SessionInsightsMetric>
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

    private static IReadOnlyList<SessionInsightsMetric> BuildSideMetrics(
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

        var sideName = SessionInsightsTextCatalog.SideName(side.Type);
        return
        [
            CreateMetric("Comp 95th", FormatSpeed(Math.Abs(side.Velocity.Percentile95Compression)), "mm/s", sideName, FormatBand(context.Profile.Compression)),
            CreateMetric("Reb 95th", FormatSpeed(Math.Abs(side.Velocity.Percentile95Rebound)), "mm/s", sideName, FormatBand(context.Profile.Rebound)),
            CreateMetric("Max travel", FormatNullablePercent(side.MaxTravelPercent), "%", sideName, ">= 85 % on hard terrain"),
        ];
    }

    private static IReadOnlyList<SessionInsightsMetric> BuildBalanceMetrics(
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

    private static SessionInsightsVibrationPanel? BuildVibrationPanel(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context)
    {
        var metrics = new List<SessionInsightsMetric>();
        AddVibrationMetrics(metrics, telemetryData, front, ImuLocation.Fork, SuspensionType.Front, context);
        AddVibrationMetrics(metrics, telemetryData, rear, ImuLocation.Shock, SuspensionType.Rear, context);

        return metrics.Count == 0
            ? null
            : new SessionInsightsVibrationPanel(
                metrics,
                "Compare vibration only side-by-side on the same trail at the same pace.");
    }

    private static void AddVibrationMetrics(
        List<SessionInsightsMetric> metrics,
        TelemetryData telemetryData,
        SideSnapshot? side,
        ImuLocation imuLocation,
        SuspensionType suspensionType,
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

        var sideName = SessionInsightsTextCatalog.SideName(suspensionType);
        metrics.Add(CreateMetric("Magic carpet ratio", FormatNumber(vibration.MagicCarpet, 2), null, sideName, null));
        metrics.Add(CreateMetric("Average g", FormatNumber(vibration.AverageGOverall, 2), "g", sideName, null));
        metrics.Add(CreateMetric("Compression vibration", FormatNumber(vibration.CompressionPercent, 1), "%", sideName, null));
        metrics.Add(CreateMetric("Rebound vibration", FormatNumber(vibration.ReboundPercent, 1), "%", sideName, null));
    }

    private static SessionInsightsMetric CreateMetric(
        string label,
        string value,
        string? unit,
        string? side,
        string? targetRange)
    {
        return new SessionInsightsMetric(label, value, unit, side, targetRange);
    }

    private static string FormatBand(SpeedBand band) => SessionInsightsTextCatalog.FormatBand(band);

    private static string FormatSpeed(double value) => SessionInsightsTextCatalog.FormatSpeed(value);

    private static string FormatNumber(double value, int decimals) => SessionInsightsTextCatalog.FormatNumber(value, decimals);

    private static string FormatNullablePercent(double? value) => SessionInsightsTextCatalog.FormatNullablePercent(value);

    private sealed record AdjustmentCandidate(
        Adjustment Adjustment,
        SessionInsightsSeverity Severity,
        SessionInsightsConfidence Confidence);
}
