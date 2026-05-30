using System;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class StrokeSpeedHistogramPlot(Plot plot, SuspensionType type, BalanceType strokeKind, SufniTheme? theme = null) : TelemetryPlot(plot, theme)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (!TelemetryStatistics.HasStrokeData(telemetryData, type, AnalysisRange))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var strokeName = strokeKind == BalanceType.Compression ? "compression" : "rebound";
        SetTitle(StatisticsPlotTitles.StrokeSpeedHistogram(type, strokeKind));
        SetAxisLabels("Peak stroke speed (mm/s)", "Strokes (%)");
        Plot.Layout.Fixed(CreateStatisticsPlotPadding());

        var data = TelemetryStatistics.CalculateStrokeSpeedHistogram(telemetryData, type, strokeKind, AnalysisRange);
        if (data.Values.Sum() <= 0)
        {
            ShowEmptyState(strokeName);
            return;
        }

        var step = data.Bins[1] - data.Bins[0];
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = data.Values.Select((value, index) => (Value: value, Index: index))
            .Where(bin => bin.Value > 0)
            .Select(bin =>
            {
                var bar = new Bar
                {
                    Position = data.Bins[bin.Index],
                    Value = bin.Value,
                    FillColor = color.WithOpacity(),
                    LineColor = color,
                    LineWidth = 1.5f,
                    Orientation = Orientation.Vertical,
                    Size = step * 0.65f,
                };

                AddBarReadout(
                    bar,
                    FormatReadoutRange("Peak speed", data.Bins, bin.Index, "mm/s", "0"),
                    new CursorReadoutLine("Strokes", bin.Value, "%", color));

                return bar;
            })
            .ToList();

        if (bars.Count > 0)
        {
            Plot.Add.Bars(bars);
        }

        var maxValue = Math.Max(1, data.Values.Max());
        var top = maxValue / 0.9;
        Plot.Axes.SetLimits(left: data.Bins[0], right: data.Bins[^1], bottom: 0, top: top);
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            data.Bins[0], data.Bins[^1], 0, top, ZoomFractions.Statistics));
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(500);
    }

    private void ShowEmptyState(string strokeName)
    {
        Plot.Axes.SetLimits(0, 1, 0, 1);
        AddLabel($"No {strokeName} strokes", 0.5, 0.5, 0, 0, Alignment.MiddleCenter);
    }
}
