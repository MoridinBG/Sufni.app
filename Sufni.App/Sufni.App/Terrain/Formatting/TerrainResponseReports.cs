using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

/// <summary>Builds the response report of Insights and of the same-trail comparison from the library's typed rows.</summary>
internal static class TerrainResponseReports
{
    private const double MinimumScale = 0.5;

    private static readonly ResponseTarget[] ForkTargets =
        [ResponseTarget.FrontRms, ResponseTarget.FrontPeakCompression, ResponseTarget.FrontPeakRebound];
    private static readonly ResponseTarget[] ShockTargets =
        [ResponseTarget.RearRms, ResponseTarget.RearPeakCompression, ResponseTarget.RearPeakRebound];
    // Vibration passed to the frame belongs to Ground & comfort, so the report leaves Transmission out.
    private static readonly ResponseTarget[] ReportedTargets = [.. ForkTargets, .. ShockTargets];

    private sealed record Measurement(ResponseCellState State, double? Deviation, string? Source = null);

    private sealed record Group(RoughnessClass? Class, ResponseConfidence Confidence, string Coverage,
        IReadOnlyDictionary<ResponseTarget, Measurement> Measurements, ImmutableArray<ResponseDetailValue> Extra = default);

    public static TerrainResponseReport AgainstUsual(IReadOnlyCollection<TerrainResponseRow> rows) => Build(
        [.. rows.Select(row => new Group(row.Class, row.Confidence, Coverage(row),
            row.Cells.ToDictionary(cell => cell.Target, cell => new Measurement(cell.State, cell.Deviation, Source(cell)))))],
        TerrainResponse.Findings(rows), TerrainResponse.RepeatRange, "your usual", "The bike behaved as usual.");

    public static TerrainResponseReport AgainstMatchedRun(IReadOnlyCollection<TerrainComparisonRow> rows) => Build(
        [.. rows.Select(row => new Group(row.Class, row.Confidence, TerrainResponseText.Distance(row.DistanceMetres),
            row.Responses.ToDictionary(pair => pair.Key, pair => new Measurement(pair.Value.State, pair.Value.Deviation)),
            [
                new("Pace", row.Pace is { } pace ? $"{TerrainResponseText.Percent(pace.Median - 1)} ({PaceRange(pace)})" : TerrainResponseText.TooShort),
                SamePosition("Fork", row.FrontSamePosition), SamePosition("Shock", row.RearSamePosition)
            ]))],
        TerrainRunComparison.Findings(rows), TerrainRunComparison.RepeatRange, "the matched run",
        "The bike behaved as on the matched run.");

    /// <summary>The pace of this run against the matched run: the median, then the middle half of the matched windows.</summary>
    public static string Pace(PaceQuartiles pace)
    {
        var median = Math.Round((pace.Median - 1) * 100);
        var relation = median == 0 ? "the same as" : $"{Math.Abs(median):0}% {(median > 0 ? "faster" : "slower")} than";
        return $"Pace: {relation} the matched run (middle half: {PaceRange(pace)}).";
    }

    private static string PaceRange(PaceQuartiles pace) =>
        $"{TerrainResponseText.Percent(pace.Lower - 1)} to {TerrainResponseText.Percent(pace.Upper - 1)}";

    private static ResponseDetailValue SamePosition(string end, ComparisonRatio ratio) =>
        new($"{end} activity at the same position", TerrainResponseText.Value(ratio.State, ratio.Deviation));

    private static TerrainResponseReport Build(IReadOnlyList<Group> groups, IEnumerable<ResponseFinding> findings,
        Func<ResponseTarget, double> repeatRange, string reference, string asUsual)
    {
        var reported = groups.Where(group => ReportedTargets.Any(target =>
                group.Measurements.TryGetValue(target, out var m) && m.State != ResponseCellState.TooShort))
            .OrderBy(group => GroupOrder(group.Class)).ToArray();
        var chart = reported.Select(group => new ResponseChartRow(group.Class, GroupLabel(group.Class), group.Coverage,
            group.Confidence, Bars(group, ForkTargets), Bars(group, ShockTargets))).ToImmutableArray();
        var verdicts = findings.Where(finding => ReportedTargets.Contains(finding.Target))
            .OrderBy(finding => GroupOrder(finding.Group)).ThenBy(finding => finding.Target)
            .Select(finding => new ResponseVerdict(finding, Verdict(finding, reference))).ToArray();
        var deviations = chart.SelectMany(row => row.Fork.Concat(row.Shock)).Select(bar => bar.Deviation).OfType<double>().ToArray();
        var summary = verdicts.Length > 0 ? null : deviations.Length > 0 ? asUsual
            : $"No response could be measured on {SuspensionResponseModel.MinimumReportMetres} m or more.";
        var scale = Math.Max(MinimumScale, Math.Ceiling(deviations.Select(Math.Abs).DefaultIfEmpty().Max() * 10) / 10);
        return new([.. verdicts.Where(v => TerrainResponseText.IsFork(v.Finding.Target))],
            [.. verdicts.Where(v => !TerrainResponseText.IsFork(v.Finding.Target))], summary, scale, chart,
            [.. reported.Select(group => new ResponseDetailRow(GroupLabel(group.Class),
                [
                    .. ReportedTargets.Where(group.Measurements.ContainsKey).Select(target =>
                    {
                        var measurement = group.Measurements[target];
                        return new ResponseDetailValue(TerrainResponseText.TargetLabel(target),
                            TerrainResponseText.Value(measurement.State, measurement.Deviation), measurement.Source);
                    }),
                    .. group.Extra.IsDefault ? [] : group.Extra
                ]))]);

        ImmutableArray<ResponseBar> Bars(Group group, ResponseTarget[] targets) =>
        [
            .. targets.Where(group.Measurements.ContainsKey).Select(target =>
            {
                var measurement = group.Measurements[target];
                return new ResponseBar(target, TerrainResponseText.Measure(target), measurement.State, measurement.Deviation,
                    repeatRange(target), TerrainResponseText.Value(measurement.State, measurement.Deviation));
            })
        ];
    }

    private static string Verdict(ResponseFinding finding, string reference)
    {
        var end = TerrainResponseText.IsFork(finding.Target) ? "Fork" : "Shock";
        var measure = TerrainResponseText.Measure(finding.Target).ToLowerInvariant();
        var verb = finding.Target is ResponseTarget.FrontRms or ResponseTarget.RearRms ? "is" : "are";
        var direction = finding.Target is ResponseTarget.FrontRms or ResponseTarget.RearRms
            ? finding.Deviation > 0 ? "higher" : "lower"
            : finding.Deviation > 0 ? "faster" : "slower";
        var group = finding.Group is { } level ? $" on {TerrainClassText.Name(level).ToLowerInvariant()} ground" : string.Empty;
        var confidence = finding.Confidence == ResponseConfidence.Low ? " (low confidence)" : string.Empty;
        return $"{end} {measure}{group} {verb} {Math.Round(Math.Abs(finding.Deviation) * 100):0}% {direction} than {reference}{confidence}.";
    }

    private static int GroupOrder(RoughnessClass? group) => group is { } level ? (int)level : -1;

    private static string GroupLabel(RoughnessClass? group) => group is { } level ? TerrainClassText.Name(level) : "All";

    private static string Coverage(TerrainResponseRow row) => row.MeanSpeed is { } speed
        ? $"{TerrainResponseText.Distance(row.DistanceMetres)} · {row.Share:P0} · {speed * 3.6:0.#} km/h"
        : TerrainResponseText.Distance(row.DistanceMetres);

    private static string Source(ResponseCell cell) =>
        $"{(cell.Predictor == ImuLocation.Fork ? "lowers" : "frame")} · {cell.BaselineSessions} rides · {cell.BaselineWindows:N0} windows";
}
