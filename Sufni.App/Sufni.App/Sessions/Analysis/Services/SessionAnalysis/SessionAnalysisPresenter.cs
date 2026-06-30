using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Presentation;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Analysis.Services.SessionAnalysis;

/// <summary>
/// Materializes the presentation contract (steps, metrics, display findings,
/// vibration panel) from a typed <see cref="SessionDiagnosticsReport"/>. Only
/// this layer references <see cref="SurfacePresentationState"/> and the
/// display records.
/// </summary>
internal static class SessionAnalysisPresenter
{
    public static SessionAnalysisResult Present(SessionDiagnosticsReport report, AnalysisContext context)
    {
        var telemetryData = context.Request.TelemetryData!;
        var presented = report.Findings
            .Select(finding => new PresentedFinding(finding, PresentFinding(finding, context)))
            .ToArray();
        var displayFindings = presented.Select(pair => pair.Display).ToArray();

        return new SessionAnalysisResult(
            SurfacePresentationState.Ready,
            BuildSteps(telemetryData, report.Front, report.Rear, context, presented),
            displayFindings.Where(finding => finding.Category == SessionAnalysisCategory.DataQuality).ToArray(),
            BuildVibrationPanel(telemetryData, report.Front, report.Rear, context),
            displayFindings);
    }

    private sealed record PresentedFinding(DiagnosticFinding Typed, SessionAnalysisFinding Display);

    private static SessionAnalysisFinding PresentFinding(DiagnosticFinding finding, AnalysisContext context)
    {
        var adjustments = finding.Adjustments
            .Select(adjustment => CreateAdjustment(finding.Id, adjustment, context))
            .ToArray();
        var evidence = finding.Evidence
            .Select(item => CreateEvidence(item, context))
            .ToArray();

        return new SessionAnalysisFinding(
            finding.Id,
            finding.Category,
            finding.Severity,
            finding.Confidence,
            SessionAnalysisTextCatalog.GetTitle(finding, context),
            SessionAnalysisTextCatalog.GetObservation(finding, context),
            BuildRecommendationText(adjustments, SessionAnalysisTextCatalog.GetFallbackRecommendation(finding, context)),
            evidence,
            adjustments);
    }

    private static Adjustment CreateAdjustment(
        SessionAnalysisFindingId findingId,
        DiagnosticAdjustment adjustment,
        AnalysisContext context)
    {
        var (magnitude, expectedEffect) = SessionAnalysisTextCatalog.GetAdjustmentText(findingId, adjustment, context);
        return new Adjustment(
            adjustment.Component,
            adjustment.Direction,
            magnitude,
            SessionAnalysisTextCatalog.SideName(adjustment.Side),
            expectedEffect,
            adjustment.Priority);
    }

    private static SessionAnalysisEvidence CreateEvidence(DiagnosticEvidence evidence, AnalysisContext context)
    {
        var (label, value, unit, sourceMode) = SessionAnalysisTextCatalog.DescribeEvidence(evidence, context);
        var side = evidence.Side is { } sideValue ? SessionAnalysisTextCatalog.SideName(sideValue) : null;
        return new SessionAnalysisEvidence(label, value, unit, side, sourceMode);
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

    private static IReadOnlyList<SessionAnalysisStep> BuildSteps(
        TelemetryData telemetryData,
        SideSnapshot? front,
        SideSnapshot? rear,
        AnalysisContext context,
        IReadOnlyList<PresentedFinding> findings)
    {
        return
        [
            BuildStep(
                SessionAnalysisStepId.Sag,
                "Sag & travel use",
                BuildSagMetrics(front, rear),
                FilterFindings(findings, IsSagFinding)),
            BuildStep(
                SessionAnalysisStepId.Fork,
                "Fork",
                BuildSideMetrics(front, context),
                FilterFindings(findings, IsForkFinding)),
            BuildStep(
                SessionAnalysisStepId.Rear,
                "Rear",
                BuildSideMetrics(rear, context),
                FilterFindings(findings, IsRearFinding)),
            BuildStep(
                SessionAnalysisStepId.Balance,
                "Balance",
                BuildBalanceMetrics(telemetryData, context),
                FilterFindings(findings, IsBalanceStepFinding)),
        ];
    }

    private static IReadOnlyList<SessionAnalysisFinding> FilterFindings(
        IReadOnlyList<PresentedFinding> findings,
        Func<DiagnosticFinding, bool> predicate)
    {
        return findings
            .Where(pair => predicate(pair.Typed))
            .Select(pair => pair.Display)
            .ToArray();
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

    private static bool IsSagFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.TravelUse ||
               (finding.Category == SessionAnalysisCategory.Balance &&
                finding.Id == SessionAnalysisFindingId.DynamicSagMismatch);
    }

    private static bool IsForkFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.ForkDamping ||
               (finding.Category == SessionAnalysisCategory.Packing && FindingSide(finding) == SuspensionType.Front);
    }

    private static bool IsRearFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.RearDamping ||
               (finding.Category == SessionAnalysisCategory.Packing && FindingSide(finding) == SuspensionType.Rear);
    }

    private static bool IsBalanceStepFinding(DiagnosticFinding finding)
    {
        return finding.Category == SessionAnalysisCategory.Balance &&
               finding.Id != SessionAnalysisFindingId.DynamicSagMismatch;
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

        var sideName = SessionAnalysisTextCatalog.SideName(side.Type);
        return
        [
            CreateMetric("Comp 95th", FormatSpeed(Math.Abs(side.Velocity.Percentile95Compression)), "mm/s", sideName, FormatBand(context.Profile.Compression)),
            CreateMetric("Reb 95th", FormatSpeed(Math.Abs(side.Velocity.Percentile95Rebound)), "mm/s", sideName, FormatBand(context.Profile.Rebound)),
            CreateMetric("Max travel", FormatNullablePercent(side.MaxTravelPercent), "%", sideName, ">= 85 % on hard terrain"),
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
        AddVibrationMetrics(metrics, telemetryData, front, ImuLocation.Fork, SuspensionType.Front, context);
        AddVibrationMetrics(metrics, telemetryData, rear, ImuLocation.Shock, SuspensionType.Rear, context);

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

        var sideName = SessionAnalysisTextCatalog.SideName(suspensionType);
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

    private static string FormatBand(SpeedBand band) => SessionAnalysisTextCatalog.FormatBand(band);

    private static string FormatSpeed(double value) => SessionAnalysisTextCatalog.FormatSpeed(value);

    private static string FormatNumber(double value, int decimals) => SessionAnalysisTextCatalog.FormatNumber(value, decimals);

    private static string FormatNullablePercent(double? value) => SessionAnalysisTextCatalog.FormatNullablePercent(value);

    private sealed record AdjustmentCandidate(
        Adjustment Adjustment,
        SessionAnalysisSeverity Severity,
        SessionAnalysisConfidence Confidence);
}
