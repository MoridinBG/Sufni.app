using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public sealed class LiveTravelPlot : LiveStreamingPlotBase
{
    private const double Floor = 5.0;
    private const double Padding = 1.1;
    private const int RenderCapacitySamples = 2048;
    private const int VisibleWindowDurationMilliseconds = 2048;

    private readonly DataStreamer frontStreamer;
    private readonly DataStreamer rearStreamer;
    private readonly TelemetryDisplayStreamingSmoother frontSmoother = new();
    private readonly TelemetryDisplayStreamingSmoother rearSmoother = new();
    private double[] frontSmoothingScratch = [];
    private double[] rearSmoothingScratch = [];
    private double runningMax;

    public LiveTravelPlot(Plot plot, double travelMaximum, bool hideRightAxis, SufniTheme? theme = null)
        : base(plot, "Travel (mm)", RenderCapacitySamples, VisibleWindowDurationMilliseconds, 0, Math.Max(1, travelMaximum), hideRightAxis, theme)
    {
        frontStreamer = CreateStreamer(TelemetryPlot.FrontColor, "Front");
        rearStreamer = CreateStreamer(TelemetryPlot.RearColor, "Rear");
        ShowSourceLegend();
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.FrontTravel.Count == 0 && batch.RearTravel.Count == 0)
        {
            return;
        }

        UpdateTiming(batch.TravelTimes);
        frontSmoother.Level = SmoothingLevel;
        rearSmoother.Level = SmoothingLevel;

        if (batch.FrontTravel.Count > 0)
        {
            frontStreamer.AddRange(frontSmoother.Apply(batch.TravelTimes, batch.FrontTravel, ref frontSmoothingScratch));
        }

        if (batch.RearTravel.Count > 0)
        {
            rearStreamer.AddRange(rearSmoother.Apply(batch.TravelTimes, batch.RearTravel, ref rearSmoothingScratch));
        }

        UpdateRunningMax(batch.FrontTravel);
        UpdateRunningMax(batch.RearTravel);
        ApplyAutoLimits();
    }

    public override void Reset()
    {
        runningMax = 0;
        frontSmoother.Reset();
        rearSmoother.Reset();
        ApplyAutoLimits();
        base.Reset();
    }

    protected override void ClearStreamers()
    {
        frontStreamer.Clear(double.NaN);
        rearStreamer.Clear(double.NaN);
    }

    private void UpdateRunningMax(IReadOnlyList<double> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i];
            if (v > runningMax)
            {
                runningMax = v;
            }
        }
    }

    private void ApplyAutoLimits()
    {
        SetVerticalLimits(0, Math.Max(runningMax * Padding, Floor));
    }
}
