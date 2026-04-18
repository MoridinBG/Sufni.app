using System;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class VelocityPlot(Plot plot) : TelemetryPlot(plot)
{
    public VerticalLine? CursorLine { get; set; }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = "Velocity (m/seconds / time )";
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        ConfigureRightAxisStyle();

        var step = 1.0 / telemetryData.Metadata.SampleRate;
        var minimum = 0.0;
        var maximum = 0.0;

        if (telemetryData.Front.Present)
        {
            var velocity = telemetryData.Front.Velocity.Select(v => v / 1000).ToArray();
            var frontSignal = Plot.Add.Signal(velocity, step, FrontColor);
            frontSignal.Axes.XAxis = Plot.Axes.Bottom;
            frontSignal.Axes.YAxis = Plot.Axes.Left;
            frontSignal.LineWidth = 2.0f;
            minimum = velocity.Min();
            maximum = velocity.Max();
        }

        if (telemetryData.Rear.Present)
        {
            var velocity = telemetryData.Rear.Velocity.Select(v => v / 1000).ToArray();
            var rearSignal = Plot.Add.Signal(velocity, step, RearColor);
            rearSignal.Axes.XAxis = Plot.Axes.Bottom;
            rearSignal.Axes.YAxis = Plot.Axes.Left;
            rearSignal.LineWidth = 2.0f;
            minimum = Math.Min(minimum, velocity.Min());
            maximum = Math.Max(maximum, velocity.Max());
        }

        // Lock the vertical, and set limits on the horizontal axis
        var ruleFront = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0, telemetryData.Metadata.Duration, minimum, maximum);
        var ruleRear = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Right,
            0, telemetryData.Metadata.Duration, minimum, maximum);
        Plot.Axes.Rules.Add(ruleFront);
        Plot.Axes.Rules.Add(ruleRear);

        ConfigureTimeTicks();
        ConfigureSymmetricValueTicks(20);

        CursorLine = Plot.Add.VerticalLine(double.NaN);
        CursorLine.LineWidth = 1;
        CursorLine.LineColor = Colors.LightGray;
    }
}