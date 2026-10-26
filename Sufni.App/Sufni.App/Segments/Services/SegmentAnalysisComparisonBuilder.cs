using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Segments.Models;
using Sufni.Telemetry;

namespace Sufni.App.Segments.Services;

/// <summary>
/// Builds the <see cref="SegmentAnalysisComparison"/> of a compared match under the
/// current analysis settings.
/// </summary>
internal static class SegmentAnalysisComparisonBuilder
{
    private const int TravelBinCount = 20;
    private const int VelocityBinCount = 40;
    private static readonly RecordedSessionPlotOverlayStyle MatchStyle = new(
        new RecordedSessionMapColor(255, 115, 199, 240),
        Width: 2,
        Opacity: 0.85);
    private static readonly RecordedSessionPlotOverlayStyle StatisticLineStyle = new(
        new RecordedSessionMapColor(255, 115, 199, 240),
        Width: 1,
        Opacity: 0.75);
    private static readonly RecordedSessionPlotLabelStyle StatisticLabelStyle = new(
        new RecordedSessionMapColor(255, 115, 199, 240),
        BackgroundColor: null,
        FontSize: 13,
        RecordedSessionPlotLabelAnchor.Right);

    public static SegmentAnalysisComparison Build(
        SegmentComparison comparison,
        SegmentComparisonDetail detail,
        TravelDistributionMode travelHistogramMode,
        VelocityAverageMode velocityAverageMode,
        DampingSpeedCutoffs cutoffs,
        SessionDampingPercentages current)
    {
        var overlays = new List<SegmentAnalysisOverlay>();
        AddTravelOverlay(overlays, comparison, detail, SegmentComparisonSeriesRole.FrontTravel, SuspensionType.Front, travelHistogramMode);
        AddTravelOverlay(overlays, comparison, detail, SegmentComparisonSeriesRole.RearTravel, SuspensionType.Rear, travelHistogramMode);
        AddVelocityOverlay(overlays, comparison, detail, SegmentComparisonSeriesRole.FrontVelocity, SuspensionType.Front, cutoffs, velocityAverageMode);
        AddVelocityOverlay(overlays, comparison, detail, SegmentComparisonSeriesRole.RearVelocity, SuspensionType.Rear, cutoffs, velocityAverageMode);

        var metrics = new List<SegmentDampingMetric>(8);
        AddMetricsForSide(metrics, comparison, current, SuspensionType.Front, SegmentComparisonSeriesRole.FrontVelocity, cutoffs.Front);
        AddMetricsForSide(metrics, comparison, current, SuspensionType.Rear, SegmentComparisonSeriesRole.RearVelocity, cutoffs.Rear);
        return new SegmentAnalysisComparison(comparison, detail, overlays, metrics);
    }

    private static void AddMetricsForSide(
        ICollection<SegmentDampingMetric> metrics,
        SegmentComparison comparison,
        SessionDampingPercentages current,
        SuspensionType side,
        SegmentComparisonSeriesRole velocityRole,
        DampingSpeedCutoffSide cutoffs)
    {
        var velocitySeries = comparison.VelocitySeries.FirstOrDefault(series => series.Role == velocityRole);
        if (velocitySeries is null || CalculateVelocityBandPercentages(velocitySeries, cutoffs) is not { } matched)
        {
            return;
        }

        foreach (var band in new[] { DampingBand.Hsr, DampingBand.Lsr, DampingBand.Lsc, DampingBand.Hsc })
        {
            if (matched.Get(band) is { } matchValue)
            {
                metrics.Add(new SegmentDampingMetric(side, band, matchValue, current.Get(side, band) - matchValue));
            }
        }
    }

    private static SessionDampingSidePercentages? CalculateVelocityBandPercentages(
        SegmentComparisonSeries velocitySeries,
        DampingSpeedCutoffSide cutoffs)
    {
        var values = velocitySeries.Values
            .Select(value => value * 1000.0)
            .Where(double.IsFinite)
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        var highSpeedCompression = 0;
        var lowSpeedCompression = 0;
        var lowSpeedRebound = 0;
        var highSpeedRebound = 0;
        foreach (var value in values)
        {
            if (value >= cutoffs.CompressionMmPerSecond)
            {
                highSpeedCompression++;
            }
            else if (value > 0)
            {
                lowSpeedCompression++;
            }
            else if (value <= -cutoffs.ReboundMmPerSecond)
            {
                highSpeedRebound++;
            }
            else if (value < 0)
            {
                lowSpeedRebound++;
            }
        }

        double Percent(int count) => count / (double)values.Length * 100.0;
        return new SessionDampingSidePercentages(
            Percent(highSpeedCompression),
            Percent(lowSpeedCompression),
            Percent(lowSpeedRebound),
            Percent(highSpeedRebound));
    }

    private static void AddTravelOverlay(
        ICollection<SegmentAnalysisOverlay> overlays,
        SegmentComparison row,
        SegmentComparisonDetail detail,
        SegmentComparisonSeriesRole role,
        SuspensionType side,
        TravelDistributionMode travelHistogramMode)
    {
        var series = row.TravelSeries.FirstOrDefault(series => series.Role == role);
        if (series is null ||
            CreateHistogramOverlay(
                series.Values,
                static value => value,
                minimum: 0,
                binCount: TravelBinCount,
                out var overlayContext) is not { } overlay)
        {
            return;
        }

        var (statisticLines, statisticLabels) =
            CreateTravelStatisticOverlays(row, detail, side, travelHistogramMode, overlayContext);
        if (statisticLines.Count > 0 || statisticLabels.Count > 0)
        {
            overlay = overlay with
            {
                Lines = overlay.Lines.Concat(statisticLines).ToArray(),
                Labels = overlay.Labels.Concat(statisticLabels).ToArray(),
            };
        }

        overlays.Add(new SegmentAnalysisOverlay(SegmentAnalysisPlot.TravelDistribution, side, overlay));
    }

    private static void AddVelocityOverlay(
        ICollection<SegmentAnalysisOverlay> overlays,
        SegmentComparison row,
        SegmentComparisonDetail detail,
        SegmentComparisonSeriesRole role,
        SuspensionType side,
        DampingSpeedCutoffs cutoffs,
        VelocityAverageMode velocityAverageMode)
    {
        var series = row.VelocitySeries.FirstOrDefault(series => series.Role == role);
        if (series is null ||
            CreateHistogramOverlay(
                series.Values,
                static value => value * 1000.0,
                minimum: null,
                binCount: VelocityBinCount,
                out var overlayContext) is not { } overlay)
        {
            return;
        }

        var (statisticLines, statisticLabels) =
            CreateVelocityStatisticOverlays(row, detail, side, cutoffs, velocityAverageMode, overlayContext);
        if (statisticLines.Count > 0 || statisticLabels.Count > 0)
        {
            overlay = overlay with
            {
                Lines = overlay.Lines.Concat(statisticLines).ToArray(),
                Labels = overlay.Labels.Concat(statisticLabels).ToArray(),
            };
        }

        overlays.Add(new SegmentAnalysisOverlay(SegmentAnalysisPlot.VelocityDistribution, side, overlay));
    }

    private static (IReadOnlyList<RecordedSessionPlotLineOverlay> Lines, IReadOnlyList<RecordedSessionPlotLabelOverlay> Labels)
        CreateVelocityStatisticOverlays(
            SegmentComparison row,
            SegmentComparisonDetail detail,
            SuspensionType side,
            DampingSpeedCutoffs cutoffs,
            VelocityAverageMode velocityAverageMode,
            HistogramOverlayContext overlayContext)
    {
        var telemetryData = detail.AnalysisTelemetryData;
        if (!TelemetryStatistics.HasStrokeData(telemetryData, side, row.AnalysisRange))
        {
            return ([], []);
        }

        var sideCutoffs = cutoffs.ForSide(side);
        var statistics = TelemetryStatistics.CalculateVelocityStatistics(
            telemetryData,
            side,
            new VelocityStatisticsOptions(
                row.AnalysisRange,
                velocityAverageMode,
                sideCutoffs.CompressionMmPerSecond,
                sideCutoffs.ReboundMmPerSecond));
        var xMaximum = Math.Max(overlayContext.XMaximum * 1.05, 1.0);
        var items = new List<(string Text, double Y, LabelSide Side)>
        {
            (string.Format(CultureInfo.InvariantCulture, "match max: {0:0.0} mm/s", statistics.MaxRebound),
                statistics.MaxRebound,
                LabelSide.Above),
            (string.Format(CultureInfo.InvariantCulture, "match avg: {0:0.0} mm/s", statistics.AverageRebound),
                statistics.AverageRebound,
                LabelSide.Below),
            (string.Format(CultureInfo.InvariantCulture, "match avg: {0:0.0} mm/s", statistics.AverageCompression),
                statistics.AverageCompression,
                LabelSide.Above),
            (string.Format(CultureInfo.InvariantCulture, "match max: {0:0.0} mm/s", statistics.MaxCompression),
                statistics.MaxCompression,
                LabelSide.Below),
        };
        if (statistics.ReboundStrokeCount > 0)
        {
            items.Insert(
                1,
                (string.Format(
                        CultureInfo.InvariantCulture,
                        "match 95%: {0:0.0} mm/s",
                        statistics.Percentile95Rebound),
                    statistics.Percentile95Rebound,
                    LabelSide.Above));
        }

        if (statistics.CompressionStrokeCount > 0)
        {
            items.Insert(
                items.Count - 1,
                (string.Format(
                        CultureInfo.InvariantCulture,
                        "match 95%: {0:0.0} mm/s",
                        statistics.Percentile95Compression),
                    statistics.Percentile95Compression,
                    LabelSide.Below));
        }

        var lines = new List<RecordedSessionPlotLineOverlay>(items.Count);
        var labels = new List<RecordedSessionPlotLabelOverlay>(items.Count);
        foreach (var item in items)
        {
            if (!double.IsFinite(item.Y))
            {
                continue;
            }

            lines.Add(new RecordedSessionPlotLineOverlay(
                0,
                item.Y,
                xMaximum,
                item.Y,
                StatisticLineStyle,
                Placement: RecordedSessionPlotLinePlacement.PlotHorizontal));
            labels.Add(new RecordedSessionPlotLabelOverlay(
                X: 0,
                item.Y,
                item.Text,
                StatisticLabelStyle with
                {
                    Anchor = item.Side == LabelSide.Above
                        ? RecordedSessionPlotLabelAnchor.Top
                        : RecordedSessionPlotLabelAnchor.Bottom,
                },
                RecordedSessionPlotLabelPlacement.PlotRightEdge));
        }

        return (lines, labels);
    }

    private static (IReadOnlyList<RecordedSessionPlotLineOverlay> Lines, IReadOnlyList<RecordedSessionPlotLabelOverlay> Labels)
        CreateTravelStatisticOverlays(
            SegmentComparison row,
            SegmentComparisonDetail detail,
            SuspensionType side,
            TravelDistributionMode travelHistogramMode,
            HistogramOverlayContext overlayContext)
    {
        var telemetryData = detail.AnalysisTelemetryData;

        if (travelHistogramMode == TravelDistributionMode.ActiveSuspension &&
            !TelemetryStatistics.HasStrokeData(telemetryData, side, row.AnalysisRange))
        {
            return ([], []);
        }

        var suspension = side == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
        if (suspension.MaxTravel is not { } maxTravel || maxTravel <= 0 || !double.IsFinite(maxTravel))
        {
            return ([], []);
        }

        var statistics = TelemetryStatistics.CalculateTravelStatistics(
            telemetryData,
            side,
            new TravelStatisticsOptions(row.AnalysisRange, travelHistogramMode));
        if (!double.IsFinite(statistics.Average) || !double.IsFinite(statistics.Max))
        {
            return ([], []);
        }

        var averagePercentage = statistics.Average / maxTravel * 100.0;
        var maxPercentage = statistics.Max / maxTravel * 100.0;
        var bottomoutLabel = travelHistogramMode == TravelDistributionMode.DynamicSag
            ? FormatCount(statistics.Bottomouts, "bottom-out region", "bottom-out regions")
            : FormatCount(statistics.Bottomouts, "stroke bottom-out", "stroke bottom-outs");
        var xMaximum = Math.Max(overlayContext.XMaximum * 1.05, 1.0);
        var items = new List<(string Text, double Y, RecordedSessionPlotLabelAnchor Anchor)>
        {
            (string.Format(
                    CultureInfo.InvariantCulture,
                    "match avg: {0:0.0} mm ({1:0.0}%)",
                    statistics.Average,
                    averagePercentage),
                statistics.Average,
                RecordedSessionPlotLabelAnchor.Top),
            (string.Format(
                    CultureInfo.InvariantCulture,
                    "match max: {0:0.0} mm ({1:0.0}%) / {2}",
                    statistics.Max,
                    maxPercentage,
                    bottomoutLabel),
                statistics.Max,
                RecordedSessionPlotLabelAnchor.Top),
        };

        var lines = new List<RecordedSessionPlotLineOverlay>(items.Count);
        var labels = new List<RecordedSessionPlotLabelOverlay>(items.Count);
        foreach (var item in items)
        {
            if (!double.IsFinite(item.Y))
            {
                continue;
            }

            lines.Add(new RecordedSessionPlotLineOverlay(
                0,
                item.Y,
                xMaximum,
                item.Y,
                StatisticLineStyle,
                Placement: RecordedSessionPlotLinePlacement.PlotHorizontal));
            labels.Add(new RecordedSessionPlotLabelOverlay(
                X: 0,
                item.Y,
                item.Text,
                StatisticLabelStyle with { Anchor = item.Anchor },
                RecordedSessionPlotLabelPlacement.PlotRightEdge));
        }

        return (lines, labels);
    }

    private static RecordedSessionAnalysisPlotOverlayDescriptor? CreateHistogramOverlay(
        IReadOnlyList<double> values,
        Func<double, double> transform,
        double? minimum,
        int binCount,
        out HistogramOverlayContext context)
    {
        context = default;
        var finiteValues = values
            .Select(transform)
            .Where(double.IsFinite)
            .ToArray();
        if (finiteValues.Length < 2)
        {
            return null;
        }

        var min = minimum ?? finiteValues.Min();
        var max = finiteValues.Max();
        if (minimum is null && min < 0 && max > 0)
        {
            var absolute = Math.Max(Math.Abs(min), Math.Abs(max));
            min = -absolute;
            max = absolute;
        }
        else if (minimum is null && min > 0)
        {
            min = 0;
        }
        else if (minimum is null && max < 0)
        {
            max = 0;
        }

        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
        {
            return null;
        }

        var counts = new double[Math.Max(2, binCount)];
        var binWidth = (max - min) / counts.Length;
        if (!double.IsFinite(binWidth) || binWidth <= 0)
        {
            return null;
        }

        foreach (var value in finiteValues)
        {
            var index = (int)((value - min) / binWidth);
            counts[Math.Clamp(index, 0, counts.Length - 1)] += 1;
        }

        var total = counts.Sum();
        if (total <= 0)
        {
            return null;
        }

        var points = counts
            .Index().Select(item => (
                X: item.Item / total * 100.0,
                Y: min + (item.Index + 0.5) * binWidth))
            .ToArray();
        context = new HistogramOverlayContext(
            points.Length == 0 ? 1 : Math.Max(1, points.Max(point => point.X)),
            min,
            max);
        var lines = new List<RecordedSessionPlotLineOverlay>(Math.Max(0, points.Length - 1));
        for (var index = 1; index < points.Length; index++)
        {
            lines.Add(new RecordedSessionPlotLineOverlay(
                points[index - 1].X,
                points[index - 1].Y,
                points[index].X,
                points[index].Y,
                MatchStyle));
        }

        return lines.Count == 0
            ? null
            : new RecordedSessionAnalysisPlotOverlayDescriptor(lines, Bands: [], Labels: []);
    }

    private enum LabelSide
    {
        Above,
        Below,
    }

    private static string FormatCount(int count, string singular, string plural) =>
        count == 1 ? $"{count} {singular}" : $"{count} {plural}";

    private readonly record struct HistogramOverlayContext(double XMaximum, double Minimum, double Maximum);
}
