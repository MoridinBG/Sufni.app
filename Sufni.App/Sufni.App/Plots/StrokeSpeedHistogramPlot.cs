using System;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class StrokeSpeedHistogramPlot(Plot plot, SuspensionType type, BalanceType strokeKind) : TelemetryPlot(plot)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (!telemetryData.HasStrokeData(type))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        var suspensionName = type == SuspensionType.Front ? "Front" : "Rear";
        var strokeName = strokeKind == BalanceType.Compression ? "compression" : "rebound";
        Plot.Axes.Title.Label.Text = $"{suspensionName} {strokeName} speed (% / mm/s)";
        Plot.Layout.Fixed(new PixelPadding(40, 10, 40, 40));

        var suspension = type == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
        var strokes = strokeKind == BalanceType.Compression
            ? suspension.Strokes.Compressions
            : suspension.Strokes.Rebounds;
        if (strokes.Length == 0)
        {
            ShowEmptyState(strokeName);
            return;
        }

        var data = telemetryData.CalculateStrokeSpeedHistogram(type, strokeKind);
        var step = data.Bins[1] - data.Bins[0];
        var color = type == SuspensionType.Front ? FrontColor : RearColor;
        var bars = data.Values.Select((value, index) => new Bar
            {
                Position = data.Bins[index],
                Value = value,
                FillColor = color.WithOpacity(),
                LineColor = color,
                LineWidth = 1.5f,
                Orientation = Orientation.Vertical,
                Size = step * 0.65f,
            })
            .ToList();

        Plot.Add.Bars(bars);

        var maxValue = Math.Max(1, data.Values.Max());
        Plot.Axes.SetLimits(left: data.Bins[0], right: data.Bins[^1], bottom: 0, top: maxValue / 0.9);
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(500);
    }

    private void ShowEmptyState(string strokeName)
    {
        Plot.Axes.SetLimits(0, 1, 0, 1);
        AddLabel($"No {strokeName} strokes", 0.5, 0.5, 0, 0, Alignment.MiddleCenter);
    }
}