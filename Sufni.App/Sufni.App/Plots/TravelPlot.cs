using System;
using ScottPlot;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class TravelPlot(Plot plot) : TelemetryPlot(plot)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = "Travel (mm / seconds)";
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        Plot.Axes.Right.TickLabelStyle.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Right.TickLabelStyle.Bold = false;
        Plot.Axes.Right.TickLabelStyle.FontSize = 12;
        Plot.Axes.Right.MajorTickStyle.Length = 0;
        Plot.Axes.Right.MinorTickStyle.Length = 0;
        Plot.Axes.Right.MajorTickStyle.Width = 0;
        Plot.Axes.Right.MinorTickStyle.Width = 0;
        
        var count = telemetryData.Front.Present ? telemetryData.Front.Travel.Length : telemetryData.Rear.Travel.Length;
        var step = 1.0 / telemetryData.Metadata.SampleRate;

        if (telemetryData.Front.Present)
        { 
            var frontSignal = Plot.Add.Signal(telemetryData.Front.Travel, step, FrontColor);
            frontSignal.Axes.XAxis = Plot.Axes.Bottom;
            frontSignal.Axes.YAxis = Plot.Axes.Left;
            frontSignal.LineWidth = 2.0f;
            
            // Lock the vertical, and set limits on the horizontal axis
            var rule = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Left, 
                0, count * step, telemetryData.Front.MaxTravel!.Value, 0);
            Plot.Axes.Rules.Add(rule);
        }

        if (telemetryData.Rear.Present)
        { 
            var rearSignal = Plot.Add.Signal(telemetryData.Rear.Travel, step, RearColor);
            rearSignal.Axes.XAxis = Plot.Axes.Bottom;
            rearSignal.Axes.YAxis = Plot.Axes.Right;
            rearSignal.LineWidth = 2.0f;
            
            // Lock the vertical, and set limits on the horizontal axis
            var rule = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Right, 
                0, count * step, telemetryData.Rear.MaxTravel!.Value, 0);
            Plot.Axes.Rules.Add(rule);
        }

        var maxTravel = Math.Max(
            telemetryData.Front.Present ? telemetryData.Front.MaxTravel!.Value : 0, 
            telemetryData.Rear.Present ? telemetryData.Rear.MaxTravel!.Value : 0);
        foreach (var airtime in  telemetryData.Airtimes)
        {
            var span = Plot.Add.HorizontalSpan(airtime.Start, airtime.End);
            span.FillColor = Color.FromHex("d53e4f").WithAlpha(0.2);
            span.LineStyle.Color = Color.FromHex("#a0a0a0").WithAlpha(0.5);
            span.LineStyle.Width = 1.0f;

            var timeSpan = airtime.End - airtime.Start;
            AddLabel($"{timeSpan:0.##}s air", airtime.Start + timeSpan / 2, maxTravel-10, 
                0, 0, Alignment.LowerCenter);
        }
        
        // Maximize tick numbers
        ScottPlot.TickGenerators.NumericAutomatic tickGenTime = new()
        {
            TargetTickCount = 20,
            LabelFormatter  = d => $"{d:0.###}"
        };
        Plot.Axes.Bottom.TickGenerator = tickGenTime;

        ScottPlot.TickGenerators.NumericAutomatic tickGenTravel = new()
        {
            MinimumTickSpacing = 20
        };
        Plot.Axes.Left.TickGenerator = tickGenTravel;
        Plot.Axes.Right.TickGenerator = tickGenTravel;
    }
}