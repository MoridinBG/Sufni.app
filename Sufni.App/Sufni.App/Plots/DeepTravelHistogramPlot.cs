using System;
using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class DeepTravelHistogramPlot(Plot plot, SuspensionType type) : TelemetryPlot(plot)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (!telemetryData.HasStrokeData(type))
        {
            return;
        }

        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = type == SuspensionType.Front
            ? "Front deep travel (strokes / mm)"
            : "Rear deep travel (strokes / mm)";
        Plot.Layout.Fixed(new PixelPadding(40, 10, 40, 40));

        var data = telemetryData.CalculateDeepTravelHistogram(type);
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
        var top = maxValue / 0.9;
        Plot.Axes.SetLimits(left: data.Bins[0], right: data.Bins[^1], bottom: 0, top: top);
        Plot.Axes.Rules.Add(new BoundedZoomRule(Plot.Axes.Bottom, Plot.Axes.Left,
            data.Bins[0], data.Bins[^1], 0, top, ZoomFractions.Statistics));
        Plot.Axes.Bottom.TickGenerator = new NumericFixedInterval(step)
        {
            LabelFormatter = value => $"{value:0.0}"
        };
    }
}