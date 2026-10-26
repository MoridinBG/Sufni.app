using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ScottPlot;
using Sufni.App.ExtensionHost.Contracts.Plots;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Plots;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Plots;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Segments.Plots;

/// <summary>
/// One signal of a matched segment on the source session's timeline, with the
/// app's signal colors.
/// </summary>
public sealed class SegmentComparisonPlot(Plot plot, SufniTheme? theme = null)
    : RecordedTimeSeriesPlot(plot, theme)
{
    private const double ValuePaddingFraction = 0.08;

    public void LoadRow(SegmentComparisonRowViewModel row)
    {
        var series = row.Series
            .Select(MapSeries)
            .OfType<RecordedTimeSeries>()
            .ToArray();
        var valueRange = ComputeRange(series, row.InvertValueAxis);

        IReadOnlyList<RecordedTimeRangeOverlaySetRegistration>? overlays =
            row.AirtimeSpans.Count > 0
                ? [RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(
                    row.AirtimeSpans.Select(airtime => (airtime.StartSeconds, airtime.EndSeconds)),
                    PlotTheme,
                    isVisible: false,
                    includeLabels: true,
                    labelY: GetAirtimeLabelY(valueRange))]
                : null;

        LoadTimeSeries(new RecordedTimeSeriesData(
            Title: string.Empty,
            EmptyMessage: row.EmptyMessage,
            DurationSeconds: row.Comparison.SourceDurationSeconds,
            Series: series,
            ValueRange: valueRange,
            InitialRangeOverlays: overlays,
            ShowLegendWhenSingleSource: false,
            EnableInteractiveLegend: false));
    }

    public override void LoadTelemetryData(TelemetryData telemetryData) =>
        throw new NotSupportedException("SegmentComparisonPlot renders matched series only.");

    private static RecordedTimeSeries? MapSeries(SegmentComparisonSeries series)
    {
        RecordedTimeSeriesValues values;
        if (series.Runs is { Count: > 0 })
        {
            var runs = series.Runs
                .Where(run => run.SourceSeconds.Length >= 2 && run.SourceSeconds.Length == run.Values.Length)
                .Select(run => new ExplicitValues(run.SourceSeconds, run.Values))
                .ToArray();
            if (runs.Length == 0)
            {
                return null;
            }

            values = new SegmentedValues(runs);
        }
        else
        {
            if (series.Values.Length < 2 || series.SourceSeconds.Length != series.Values.Length)
            {
                return null;
            }

            values = new ExplicitValues(series.SourceSeconds, series.Values);
        }

        return new RecordedTimeSeries(
            series.Label,
            series.Unit,
            ColorFor(series.Role),
            values,
            series.Format,
            LineWidth: 1.6f,
            SourceKey: series.Role.ToString());
    }

    private static RecordedTimeSeriesValueRange ComputeRange(IReadOnlyList<RecordedTimeSeries> series, bool invert)
    {
        var values = series
            .SelectMany(GetYValues)
            .Where(double.IsFinite)
            .ToArray();
        double min, max;
        if (values.Length == 0)
        {
            (min, max) = (0, 1);
        }
        else
        {
            min = values.Min();
            max = values.Max();
            if (min == max)
            {
                min -= 1;
                max += 1;
            }
            else
            {
                var padding = (max - min) * ValuePaddingFraction;
                min -= padding;
                max += padding;
            }
        }

        return invert
            ? new RecordedTimeSeriesValueRange(max, min)
            : new RecordedTimeSeriesValueRange(min, max);
    }

    private static IEnumerable<double> GetYValues(RecordedTimeSeries series) => series.Values switch
    {
        ExplicitValues values => values.YValues,
        SegmentedValues values => values.Segments.SelectMany(segment => segment.YValues),
        _ => throw new UnreachableException(),
    };

    private static Color ColorFor(SegmentComparisonSeriesRole role) => role switch
    {
        SegmentComparisonSeriesRole.FrontTravel or SegmentComparisonSeriesRole.FrontVelocity => FrontColor,
        SegmentComparisonSeriesRole.RearTravel or SegmentComparisonSeriesRole.RearVelocity => RearColor,
        SegmentComparisonSeriesRole.FrameImu => ImuPlot.FrameColor,
        SegmentComparisonSeriesRole.ForkImu => FrontColor,
        SegmentComparisonSeriesRole.ShockImu => RearColor,
        _ => SufniThemes.SignalSeries.GpsSpeed.ToScottPlotColor(),
    };
}
