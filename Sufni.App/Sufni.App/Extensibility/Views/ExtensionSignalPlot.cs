using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.App.ExtensionHost.Contracts.Plots;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;

using Sufni.App.Sessions.Plots;
using Sufni.App.Theming;
using Sufni.App.LiveDaq.Plots;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Extensibility.Views;

// App-rendered plot for a neutral extension-contributed signal plot. Maps the
// neutral RecordedSessionSignalPlotViewModel onto RecordedTimeSeriesPlot so
// extension-provided series rows can inherit app plot styling instead of
// drawing with extension-owned hex literals.
public sealed class ExtensionSignalPlot(Plot plot, SufniTheme? theme = null)
    : RecordedTimeSeriesPlot(plot, theme)
{
    public void LoadSeries(RecordedSessionSignalPlotViewModel vm)
    {
        var series = vm.Series
            .Select(MapSeries)
            .Where(series => series is not null)
            .Cast<RecordedTimeSeries>()
            .ToArray();

        var valueRange = ComputeRange(series, vm.InvertValueAxis);

        IReadOnlyList<RecordedTimeRangeOverlaySetRegistration>? overlays =
            vm.AirtimeSpans.Count > 0
                ? [RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(
                    vm.AirtimeSpans.Select(a => (a.StartSeconds, a.EndSeconds)),
                    PlotTheme,
                    isVisible: false,            // visibility driven by the view's ShowAirtime
                    includeLabels: true,
                    labelY: GetAirtimeLabelY(valueRange))]
                : null;

        LoadTimeSeries(new RecordedTimeSeriesData(
            Title: string.Empty,                 // suppressed by ConfigureTimeSeriesFrame
            EmptyMessage: vm.EmptyMessage,
            DurationSeconds: vm.DurationSeconds,
            Series: series,
            ValueRange: valueRange,
            InitialRangeOverlays: overlays,
            ShowLegendWhenSingleSource: false,
            EnableInteractiveLegend: false));
    }

    public override void LoadTelemetryData(TelemetryData telemetryData) =>
        throw new NotSupportedException("ExtensionSignalPlot renders neutral series only.");

    private static RecordedTimeSeries? MapSeries(RecordedSessionSignalSeries series)
    {
        RecordedTimeSeriesValues values;
        if (series.Runs is { Count: > 0 })
        {
            var runs = series.Runs
                .Where(run => run.SecondsX.Length >= 2 && run.SecondsX.Length == run.Values.Length)
                .Select(run => new ExplicitValues(run.SecondsX, run.Values))
                .ToArray();
            if (runs.Length == 0)
            {
                return null;
            }

            values = new SegmentedValues(runs);
        }
        else
        {
            if (series.Values.Length < 2 || series.SecondsX.Length != series.Values.Length)
            {
                return null;
            }

            values = new ExplicitValues(series.SecondsX, series.Values);
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

    private static RecordedTimeSeriesValueRange ComputeRange(
        IReadOnlyList<RecordedTimeSeries> series, bool invert)
    {
        var ys = series
            .SelectMany(GetYValues)
            .Where(double.IsFinite)
            .ToArray();
        double min, max;
        if (ys.Length == 0) { (min, max) = (0, 1); }
        else
        {
            min = ys.Min(); max = ys.Max();
            if (min == max) { min -= 1; max += 1; }
            else { var pad = (max - min) * 0.08; min -= pad; max += pad; }
        }
        return invert
            ? new RecordedTimeSeriesValueRange(max, min)
            : new RecordedTimeSeriesValueRange(min, max);
    }

    internal static IEnumerable<double> GetYValues(RecordedTimeSeries series) => series.Values switch
    {
        SampledValues values => values.Samples,
        ExplicitValues values => values.YValues,
        SegmentedValues values => values.Segments.SelectMany(segment => segment.YValues),
        _ => throw new NotSupportedException(
            $"Unsupported recorded time-series values type '{series.Values.GetType().FullName}'."),
    };

    private static Color ColorFor(RecordedSessionSignalSeriesRole role) => role switch
    {
        RecordedSessionSignalSeriesRole.FrontSuspension => FrontColor,
        RecordedSessionSignalSeriesRole.RearSuspension  => RearColor,
        RecordedSessionSignalSeriesRole.FrameImu        => ImuPlot.FrameColor,
        RecordedSessionSignalSeriesRole.ForkImu         => FrontColor,
        RecordedSessionSignalSeriesRole.ShockImu        => RearColor,
        RecordedSessionSignalSeriesRole.GpsSpeed        => SufniThemes.SignalSeries.GpsSpeed.ToScottPlotColor(),
        _ => Colors.Gray,
    };
}
