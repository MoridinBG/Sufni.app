using System;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class TravelPlot(Plot plot) : TelemetryPlot(plot)
{
    private HorizontalSpan? selectedSpan;
    private HorizontalSpan? previewSpan;

    public VerticalLine? CursorLine { get; set; }

    public void SetAnalysisRange(TelemetryTimeRange? range)
    {
        selectedSpan = SetSpan(selectedSpan, range?.StartSeconds, range?.EndSeconds,
            FrontColor.WithAlpha(0.16));
    }

    public void SetPreviewRange(double? startSeconds, double? endSeconds)
    {
        previewSpan = SetSpan(previewSpan, startSeconds, endSeconds, Colors.LightGray.WithAlpha(0.12));
    }

    public override void SetCursorPosition(double position)
    {
        if (CursorLine is not null)
        {
            CursorLine.Position = position;
        }
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        base.LoadTelemetryData(telemetryData);

        CursorLine = null;
        selectedSpan = null;
        previewSpan = null;

        Plot.Axes.Title.Label.Text = "Travel (mm / seconds)";
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        ConfigureRightAxisStyle();

        if (telemetryData.Front.Present)
        {
            var (frontTravel, frontStep) = PrepareDisplaySignal(telemetryData.Front.Travel, telemetryData.Metadata.SampleRate);
            var frontSignal = Plot.Add.Signal(frontTravel, frontStep, FrontColor);
            frontSignal.Axes.XAxis = Plot.Axes.Bottom;
            frontSignal.Axes.YAxis = Plot.Axes.Left;
            frontSignal.LineWidth = 2.0f;

            // Lock the vertical, and set limits on the horizontal axis
            var rule = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Left,
                0, telemetryData.Metadata.Duration, telemetryData.Front.MaxTravel!.Value, 0);
            Plot.Axes.Rules.Add(rule);
        }

        if (telemetryData.Rear.Present)
        {
            var (rearTravel, rearStep) = PrepareDisplaySignal(telemetryData.Rear.Travel, telemetryData.Metadata.SampleRate);
            var rearSignal = Plot.Add.Signal(rearTravel, rearStep, RearColor);
            rearSignal.Axes.XAxis = Plot.Axes.Bottom;
            rearSignal.Axes.YAxis = Plot.Axes.Right;
            rearSignal.LineWidth = 2.0f;

            // Lock the vertical, and set limits on the horizontal axis
            var rule = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Right,
                0, telemetryData.Metadata.Duration, telemetryData.Rear.MaxTravel!.Value, 0);
            Plot.Axes.Rules.Add(rule);
        }

        var maxTravel = Math.Max(
            telemetryData.Front.Present ? telemetryData.Front.MaxTravel!.Value : 0,
            telemetryData.Rear.Present ? telemetryData.Rear.MaxTravel!.Value : 0);
        foreach (var airtime in telemetryData.Airtimes)
        {
            var span = Plot.Add.HorizontalSpan(airtime.Start, airtime.End);
            span.FillColor = Color.FromHex("d53e4f").WithAlpha(0.2);
            span.LineStyle.Color = Color.FromHex("#a0a0a0").WithAlpha(0.5);
            span.LineStyle.Width = 1.0f;

            var timeSpan = airtime.End - airtime.Start;
            AddLabel($"{timeSpan:0.##}s air", airtime.Start + timeSpan / 2, maxTravel - 10,
                0, 0, Alignment.LowerCenter);
        }

        AddMarkerLines(telemetryData);

        ConfigureTimeTicks();
        ConfigureSymmetricValueTicks(20);

        SetAnalysisRange(AnalysisRange);
        SetPreviewRange(null, null);

        CursorLine = Plot.Add.VerticalLine(double.NaN);
        CursorLine.LineWidth = 1;
        CursorLine.LineColor = Colors.LightGray;
    }

    private HorizontalSpan? SetSpan(HorizontalSpan? span, double? startSeconds, double? endSeconds, Color color)
    {
        if (startSeconds is null || endSeconds is null)
        {
            if (span is not null)
            {
                span.IsVisible = false;
            }

            return span;
        }

        var start = Math.Min(startSeconds.Value, endSeconds.Value);
        var end = Math.Max(startSeconds.Value, endSeconds.Value);
        if (span is null)
        {
            span = Plot.Add.HorizontalSpan(start, end);
            span.FillColor = color;
            span.LineStyle.Width = 0;
            span.EnableAutoscale = false;
        }
        else
        {
            span.X1 = start;
            span.X2 = end;
            span.FillColor = color;
        }

        span.IsVisible = true;
        return span;
    }
}
