using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class VelocityPlot(Plot plot) : TelemetryPlot(plot)
{
    private readonly List<CursorReadoutSeries> cursorSeries = [];
    private double cursorDurationSeconds;

    public VerticalLine? CursorLine { get; set; }

    protected override void SetCursorLinePosition(double position)
    {
        if (CursorLine is not null)
        {
            CursorLine.Position = position;
        }
    }

    protected override CursorReadout? GetCursorReadout(double position)
    {
        return CreateCursorReadout(position, cursorDurationSeconds, cursorSeries);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        base.LoadTelemetryData(telemetryData);
        cursorSeries.Clear();
        cursorDurationSeconds = telemetryData.Metadata.Duration;

        Plot.Axes.Title.Label.Text = "Velocity (m/seconds / time )";
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        ConfigureRightAxisStyle();
        var minimum = 0.0;
        var maximum = 0.0;

        if (telemetryData.Front.Present)
        {
            var fullVelocity = telemetryData.Front.Velocity.Select(v => v / 1000).ToArray();
            var (velocity, step) = PrepareDisplaySignal(fullVelocity, telemetryData.Metadata.SampleRate);
            cursorSeries.Add(CursorReadoutSeries.FromRegularSamples(
                "Front",
                "m/s",
                FrontColor,
                velocity,
                step,
                telemetryData.Metadata.Duration,
                "0.###"));
            var frontSignal = Plot.Add.Signal(velocity, step, FrontColor);
            frontSignal.Axes.XAxis = Plot.Axes.Bottom;
            frontSignal.Axes.YAxis = Plot.Axes.Left;
            frontSignal.LineWidth = 2.0f;
            minimum = fullVelocity.Min();
            maximum = fullVelocity.Max();
        }

        if (telemetryData.Rear.Present)
        {
            var fullVelocity = telemetryData.Rear.Velocity.Select(v => v / 1000).ToArray();
            var (velocity, step) = PrepareDisplaySignal(fullVelocity, telemetryData.Metadata.SampleRate);
            cursorSeries.Add(CursorReadoutSeries.FromRegularSamples(
                "Rear",
                "m/s",
                RearColor,
                velocity,
                step,
                telemetryData.Metadata.Duration,
                "0.###"));
            var rearSignal = Plot.Add.Signal(velocity, step, RearColor);
            rearSignal.Axes.XAxis = Plot.Axes.Bottom;
            rearSignal.Axes.YAxis = Plot.Axes.Left;
            rearSignal.LineWidth = 2.0f;
            minimum = Math.Min(minimum, fullVelocity.Min());
            maximum = Math.Max(maximum, fullVelocity.Max());
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

        AddMarkerLines(telemetryData);

        CursorLine = Plot.Add.VerticalLine(double.NaN);
        CursorLine.LineWidth = 1;
        CursorLine.LineColor = Colors.LightGray;
    }
}
