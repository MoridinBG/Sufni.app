using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Segments.Models;
using Sufni.App.Shared.Plots;
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
            CreateHistogramTrace(series.Values, static value => value, minimum: 0, TravelBinCount) is not { } trace)
        {
            return;
        }

        var markers = CreateTravelStatisticMarkers(row, detail, side, travelHistogramMode);
        overlays.Add(new SegmentAnalysisOverlay(
            SegmentAnalysisPlot.TravelDistribution,
            side,
            new AnalysisPlotOverlay(trace.X, trace.Y, markers)));
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
            CreateHistogramTrace(series.Values, static value => value * 1000.0, minimum: null, VelocityBinCount) is not { } trace)
        {
            return;
        }

        var markers = CreateVelocityStatisticMarkers(row, detail, side, cutoffs, velocityAverageMode);
        overlays.Add(new SegmentAnalysisOverlay(
            SegmentAnalysisPlot.VelocityDistribution,
            side,
            new AnalysisPlotOverlay(trace.X, trace.Y, markers)));
    }

    private static IReadOnlyList<AnalysisPlotMarker> CreateVelocityStatisticMarkers(
        SegmentComparison row,
        SegmentComparisonDetail detail,
        SuspensionType side,
        DampingSpeedCutoffs cutoffs,
        VelocityAverageMode velocityAverageMode)
    {
        var telemetryData = detail.AnalysisTelemetryData;
        if (!TelemetryStatistics.HasStrokeData(telemetryData, side, row.AnalysisRange))
        {
            return [];
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
        var markers = new List<AnalysisPlotMarker>
        {
            new(statistics.MaxRebound,
                string.Format(CultureInfo.InvariantCulture, "match max: {0:0.0} mm/s", statistics.MaxRebound),
                LabelAbove: true),
            new(statistics.AverageRebound,
                string.Format(CultureInfo.InvariantCulture, "match avg: {0:0.0} mm/s", statistics.AverageRebound),
                LabelAbove: false),
            new(statistics.AverageCompression,
                string.Format(CultureInfo.InvariantCulture, "match avg: {0:0.0} mm/s", statistics.AverageCompression),
                LabelAbove: true),
            new(statistics.MaxCompression,
                string.Format(CultureInfo.InvariantCulture, "match max: {0:0.0} mm/s", statistics.MaxCompression),
                LabelAbove: false),
        };
        if (statistics.ReboundStrokeCount > 0)
        {
            markers.Insert(
                1,
                new AnalysisPlotMarker(
                    statistics.Percentile95Rebound,
                    string.Format(CultureInfo.InvariantCulture, "match 95%: {0:0.0} mm/s", statistics.Percentile95Rebound),
                    LabelAbove: true));
        }

        if (statistics.CompressionStrokeCount > 0)
        {
            markers.Insert(
                markers.Count - 1,
                new AnalysisPlotMarker(
                    statistics.Percentile95Compression,
                    string.Format(CultureInfo.InvariantCulture, "match 95%: {0:0.0} mm/s", statistics.Percentile95Compression),
                    LabelAbove: false));
        }

        return markers.Where(marker => double.IsFinite(marker.Y)).ToArray();
    }

    private static IReadOnlyList<AnalysisPlotMarker> CreateTravelStatisticMarkers(
        SegmentComparison row,
        SegmentComparisonDetail detail,
        SuspensionType side,
        TravelDistributionMode travelHistogramMode)
    {
        var telemetryData = detail.AnalysisTelemetryData;

        if (travelHistogramMode == TravelDistributionMode.ActiveSuspension &&
            !TelemetryStatistics.HasStrokeData(telemetryData, side, row.AnalysisRange))
        {
            return [];
        }

        var suspension = side == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
        if (suspension.MaxTravel is not { } maxTravel || maxTravel <= 0 || !double.IsFinite(maxTravel))
        {
            return [];
        }

        var statistics = TelemetryStatistics.CalculateTravelStatistics(
            telemetryData,
            side,
            new TravelStatisticsOptions(row.AnalysisRange, travelHistogramMode));
        if (!double.IsFinite(statistics.Average) || !double.IsFinite(statistics.Max))
        {
            return [];
        }

        var averagePercentage = statistics.Average / maxTravel * 100.0;
        var maxPercentage = statistics.Max / maxTravel * 100.0;
        var bottomoutLabel = travelHistogramMode == TravelDistributionMode.DynamicSag
            ? FormatCount(statistics.Bottomouts, "bottom-out region", "bottom-out regions")
            : FormatCount(statistics.Bottomouts, "stroke bottom-out", "stroke bottom-outs");
        return
        [
            new AnalysisPlotMarker(
                statistics.Average,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "match avg: {0:0.0} mm ({1:0.0}%)",
                    statistics.Average,
                    averagePercentage),
                LabelAbove: true),
            new AnalysisPlotMarker(
                statistics.Max,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "match max: {0:0.0} mm ({1:0.0}%) / {2}",
                    statistics.Max,
                    maxPercentage,
                    bottomoutLabel),
                LabelAbove: true),
        ];
    }

    /// <summary>
    /// Bins the values and returns the share of each bin in percent (X) against
    /// the bin center (Y), or null when the values do not form a histogram.
    /// </summary>
    private static (double[] X, double[] Y)? CreateHistogramTrace(
        IReadOnlyList<double> values,
        Func<double, double> transform,
        double? minimum,
        int binCount)
    {
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

        return (
            counts.Select(count => count / total * 100.0).ToArray(),
            counts.Select((_, index) => min + (index + 0.5) * binWidth).ToArray());
    }

    private static string FormatCount(int count, string singular, string plural) =>
        count == 1 ? $"{count} {singular}" : $"{count} {plural}";
}
