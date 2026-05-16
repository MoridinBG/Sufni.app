using System;
using System.Globalization;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class TravelFrequencyHistogramPlot(Plot plot, SuspensionType type) : TelemetryPlot(plot)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (!TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = type == SuspensionType.Front
            ? "Front frequencies"
            : "Rear frequencies";
        SetAxisLabels("Frequency (Hz)", "Power (dB)");
        Plot.Layout.Fixed(new PixelPadding(65, 10, 55, 40));

        var data = TelemetryStatistics.CalculateTravelFrequencyHistogram(telemetryData, type, AnalysisRange);
        if (data.Bins.Count == 0 || data.Values.Count == 0)
        {
            Plot.Axes.SetLimits(0, 1, 0, 1);
            AddLabel("No frequency data", 0.5, 0.5, 0, 0, Alignment.MiddleCenter);
            return;
        }
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = data.Values.Select((value, index) =>
            {
                var bar = new Bar
                {
                    Position = data.Bins[index],
                    Value = value,
                    FillColor = color.WithOpacity(),
                    LineColor = color,
                    LineWidth = 1.5f,
                    Orientation = Orientation.Vertical,
                    Size = 4.9 / data.Bins.Count
                };
                var powerDb = value > 0 ? 20 * Math.Log10(value) : double.NaN;
                var powerLine = double.IsFinite(powerDb)
                    ? new CursorReadoutLine("Power", powerDb, "dB", color, "0.#")
                    : new CursorReadoutLine("Power", value, string.Empty, color, "0.###");

                AddBarReadout(
                    bar,
                    $"Frequency: {FormatReadoutValue(data.Bins[index], "Hz", "0.##")}",
                    powerLine);

                return bar;
            })
            .ToList();

        Plot.Add.Bars(bars);

        // Set axis initial range and limits
        var min = data.Values.Min();
        var max = data.Values.Max();
        Plot.Axes.SetLimits(left: 0.0, right: 800.0 / data.Bins.Count * 3.0, bottom: min, top: max);
        Plot.Axes.Rules.Add(new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0.0, 10.0, min, max, ZoomFractions.Statistics));

        // Add autoscaler that restores the original ranges
        Plot.Axes.AutoScaler = new FixedAutoScaler(minX: 0.0, maxX: 800.0 / data.Bins.Count * 3.0);

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
