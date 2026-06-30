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

// App-rendered plot for a neutral extension-contributed series graph. Maps the
// neutral RecordedSessionSeriesGraphViewModel onto RecordedTimeSeriesPlot so
// extension-provided series rows can inherit app plot styling instead of
// drawing with extension-owned hex literals.
public sealed class ExtensionSeriesGraphPlot(Plot plot, SufniTheme? theme = null)
    : RecordedTimeSeriesPlot(plot, theme)
{
    public void LoadSeries(RecordedSessionSeriesGraphViewModel vm)
    {
        var series = vm.Series
            .Where(s => s.Values.Length >= 2 && s.SecondsX.Length == s.Values.Length)
            .Select(s => new RecordedTimeSeries(
                s.Label, s.Unit, ColorFor(s.Role),
                new ExplicitValues(s.SecondsX, s.Values),
                s.Format, LineWidth: 1.6f, SourceKey: s.Role.ToString()))
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
        throw new NotSupportedException("ExtensionSeriesGraphPlot renders neutral series only.");

    private static RecordedTimeSeriesValueRange ComputeRange(
        IReadOnlyList<RecordedTimeSeries> series, bool invert)
    {
        var ys = series
            .SelectMany(s => ((ExplicitValues)s.Values).YValues)
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

    private static Color ColorFor(RecordedSessionGraphSeriesRole role) => role switch
    {
        RecordedSessionGraphSeriesRole.SuspensionFront => FrontColor,
        RecordedSessionGraphSeriesRole.SuspensionRear  => RearColor,
        RecordedSessionGraphSeriesRole.ImuFrame        => ImuPlot.FrameColor,
        RecordedSessionGraphSeriesRole.ImuFork         => FrontColor,
        RecordedSessionGraphSeriesRole.ImuShock        => RearColor,
        RecordedSessionGraphSeriesRole.GpsSpeed        => SufniThemes.SignalSeries.GpsSpeed.ToScottPlotColor(),
        _ => Colors.Gray,
    };
}
