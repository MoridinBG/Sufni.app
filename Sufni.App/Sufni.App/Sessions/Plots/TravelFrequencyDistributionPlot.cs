using System;
using System.Globalization;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Shared.Plots;
using Sufni.App.Theming;
namespace Sufni.App.Sessions.Plots;

public class TravelFrequencyDistributionPlot(Plot plot, SuspensionType type, SufniTheme? theme = null) : TelemetryPlot(plot, theme)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var hasStrokeData = TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange);
        LoadAnalysisData(new TravelFrequencyDistributionAnalysisResult(
            hasStrokeData
                ? TelemetryStatistics.CalculateTravelFrequencyHistogram(telemetryData, type, AnalysisRange)
                : new HistogramData([], []),
            hasStrokeData));
    }

    public void LoadAnalysisData(TravelFrequencyDistributionAnalysisResult data)
    {
        if (!data.HasStrokeData)
        {
            return;
        }

        ResetTelemetryReadouts();

        SetTitle(AnalysisPlotTitles.TravelFrequencyDistribution(type));
        SetAxisLabels("Frequency (Hz)", "Power (dB)");
        Plot.Layout.Fixed(CreateAnalysisPlotPadding());

        var histogram = data.Histogram;
        if (histogram.Bins.Count == 0 || histogram.Values.Count == 0)
        {
            Plot.Axes.SetLimits(0, 1, 0, 1);
            AddLabel("No frequency data", 0.5, 0.5, 0, 0, Alignment.MiddleCenter);
            return;
        }
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = histogram.Values.Index().Select(item =>
            {
                var bar = new Bar
                {
                    Position = histogram.Bins[item.Index],
                    Value = item.Item,
                    FillColor = color.WithOpacity(),
                    LineColor = color,
                    LineWidth = 1.5f,
                    Orientation = Orientation.Vertical,
                    Size = 4.9 / histogram.Bins.Count
                };
                var powerDb = item.Item > 0 ? 20 * Math.Log10(item.Item) : double.NaN;
                var powerLine = double.IsFinite(powerDb)
                    ? new CursorReadoutLine("Power", powerDb, "dB", color, "0.#")
                    : new CursorReadoutLine("Power", item.Item, string.Empty, color, "0.###");

                AddBarReadout(
                    bar,
                    $"Frequency: {FormatReadoutValue(histogram.Bins[item.Index], "Hz", "0.##")}",
                    powerLine);

                return bar;
            })
            .ToList();

        Plot.Add.Bars(bars);

        // Set axis initial range and limits
        var min = histogram.Values.Min();
        var max = histogram.Values.Max();
        Plot.Axes.SetLimits(left: 0.0, right: 800.0 / histogram.Bins.Count * 3.0, bottom: min, top: max);
        Plot.Axes.Rules.Add(new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0.0, 10.0, min, max, ZoomFractions.Analysis));

        // Add autoscaler that restores the original ranges
        Plot.Axes.AutoScaler = new FixedAutoScaler(minX: 0.0, maxX: 800.0 / histogram.Bins.Count * 3.0);

        // Generate 4 tick for the power axis, and display its 20*log10 value
        var tickSpacing = (max - min) / 3;
        var values = new[] { min, min + tickSpacing, min + 2 * tickSpacing, max };
        var labels = values.Select(v => Math.Floor(20 * Math.Log10(v)).ToString(CultureInfo.InvariantCulture));
        Plot.Axes.Left.SetTicks([.. values], [.. labels]);

        Plot.Axes.Bottom.TickGenerator = new NumericAutomatic
        {
            TickDensity = 0.2
        };
    }
}
