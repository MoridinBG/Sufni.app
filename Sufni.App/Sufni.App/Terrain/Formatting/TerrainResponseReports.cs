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

    private sealed record Measurement(ResponseCellState State, double? Deviation);

    private sealed record Group(RoughnessClass? Class, ResponseConfidence Confidence, string Coverage, string? CoverageNote,
        IReadOnlyDictionary<ResponseTarget, Measurement> Measurements, ResponseBar? ForkExtra = null, ResponseBar? ShockExtra = null);

    public static TerrainResponseReport AgainstUsual(IReadOnlyCollection<TerrainResponseRow> rows) => Build(
        [.. rows.Select(row => new Group(row.Class, row.Confidence, Coverage(row), null,
            row.Cells.ToDictionary(cell => cell.Target, cell => new Measurement(cell.State, cell.Deviation))))],
        TerrainResponse.Findings(rows), TerrainResponse.RepeatRange, "your usual", "The bike behaved as usual.",
        "Each bar compares this ride with what your usual predicts on the same ground at the same speed. " +
        "The shaded band is the normal spread between rides; a highlighted bar falls outside it.");

    public static TerrainResponseReport AgainstMatchedRun(IReadOnlyCollection<TerrainComparisonRow> rows) => Build(
        [.. rows.Select(row => new Group(row.Class, row.Confidence,
            row.Pace is { } pace ? $"{TerrainResponseText.Distance(row.DistanceMetres)} · pace {TerrainResponseText.Percent(pace.Median - 1)}"
                : TerrainResponseText.Distance(row.DistanceMetres),
            row.Pace is { } range ? $"The middle half of this ground was {PaceRange(range)} against the matched run." : null,
            row.Responses.ToDictionary(pair => pair.Key, pair => new Measurement(pair.Value.State, pair.Value.Deviation)),
            SamePosition(row.FrontSamePosition), SamePosition(row.RearSamePosition)))],
        TerrainRunComparison.Findings(rows), TerrainRunComparison.RepeatRange, "the matched run",
        "The bike behaved as on the matched run.",
        "Each bar compares this run with the matched run on the same ground, with the speed difference taken out. " +
        "The shaded band is the normal spread between runs; a highlighted bar falls outside it.");

    /// <summary>The pace of this run against the matched run: the median, then the middle half of the matched windows.</summary>
    public static string Pace(PaceQuartiles pace)
    {
        var median = Math.Round((pace.Median - 1) * 100);
        var relation = median == 0 ? "the same as" : $"{Math.Abs(median):0}% {(median > 0 ? "faster" : "slower")} than";
        return $"Pace: {relation} the matched run (middle half: {PaceRange(pace)}).";
    }

    private static string PaceRange(PaceQuartiles pace) =>
        $"{TerrainResponseText.Percent(pace.Lower - 1)} to {TerrainResponseText.Percent(pace.Upper - 1)}";

    private static ResponseBar SamePosition(ComparisonRatio ratio) =>
        new(null, TerrainResponseText.SamePosition, TerrainResponseText.SamePositionExplanation, ratio.State, ratio.Deviation,
            TerrainRunComparison.ResponseThreshold, TerrainResponseText.Value(ratio.State, ratio.Deviation));

    private static string? ConfidenceNote(ResponseConfidence confidence) =>
        confidence == ResponseConfidence.Low ? TerrainResponseText.LowConfidenceNote : null;

    private static TerrainResponseReport Build(IReadOnlyList<Group> groups, IEnumerable<ResponseFinding> findings,
        Func<ResponseTarget, double> repeatRange, string reference, string asUsual, string guide)
    {
        var reported = groups.Where(group => ReportedTargets.Any(target =>
                group.Measurements.TryGetValue(target, out var m) && m.State != ResponseCellState.TooShort))
            .OrderBy(group => GroupOrder(group.Class)).ToArray();
        var chart = reported.Select(group => new ResponseChartRow(group.Class, GroupLabel(group.Class), group.Coverage,
            group.CoverageNote, ConfidenceNote(group.Confidence), Bars(group, ForkTargets, group.ForkExtra),
            Bars(group, ShockTargets, group.ShockExtra))).ToImmutableArray();
        var verdicts = findings.Where(finding => ReportedTargets.Contains(finding.Target))
            .OrderBy(finding => GroupOrder(finding.Group)).ThenBy(finding => finding.Target)
            .Select(finding => new ResponseVerdict(finding, Verdict(finding, reference), ConfidenceNote(finding.Confidence))).ToArray();
        var deviations = chart.SelectMany(row => row.Fork.Concat(row.Shock)).Select(bar => bar.Deviation).OfType<double>().ToArray();
        var summary = verdicts.Length > 0 ? null : deviations.Length > 0 ? asUsual
            : $"No response could be measured on {SuspensionResponseModel.MinimumReportMetres} m or more.";
        var scale = Math.Max(MinimumScale, Math.Ceiling(deviations.Select(Math.Abs).DefaultIfEmpty().Max() * 10) / 10);
        return new([.. verdicts.Where(v => TerrainResponseText.IsFork(v.Finding.Target))],
            [.. verdicts.Where(v => !TerrainResponseText.IsFork(v.Finding.Target))], summary, guide, scale,
            TerrainResponseText.Percent(-scale), TerrainResponseText.Percent(scale), chart);

        ImmutableArray<ResponseBar> Bars(Group group, ResponseTarget[] targets, ResponseBar? extra) =>
        [
            .. targets.Where(group.Measurements.ContainsKey).Select(target =>
            {
                var measurement = group.Measurements[target];
                return new ResponseBar(target, TerrainResponseText.Measure(target), TerrainResponseText.Explanation(target),
                    measurement.State, measurement.Deviation, repeatRange(target),
                    TerrainResponseText.Value(measurement.State, measurement.Deviation));
            }),
            .. extra is null ? [] : new[] { extra }
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
        return $"{end} {measure}{group} {verb} {Math.Round(Math.Abs(finding.Deviation) * 100):0}% {direction} than {reference}.";
    }

    private static int GroupOrder(RoughnessClass? group) => group is { } level ? (int)level : -1;

    private static string GroupLabel(RoughnessClass? group) => group is { } level ? TerrainClassText.Name(level) : "All descents";

    private static string Coverage(TerrainResponseRow row) => row.Class is null
        ? Speed(TerrainResponseText.Distance(row.DistanceMetres), row.MeanSpeed)
        : Speed($"{TerrainResponseText.Distance(row.DistanceMetres)} · {row.Share:P0} of descents", row.MeanSpeed);

    private static string Speed(string coverage, double? metresPerSecond) =>
        metresPerSecond is { } speed ? $"{coverage} · {speed * 3.6:0.#} km/h" : coverage;
}
