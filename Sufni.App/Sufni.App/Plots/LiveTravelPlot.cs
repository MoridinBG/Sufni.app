using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Plots;

public sealed class LiveTravelPlot : LiveStreamingPlotBase
{
    private const double Floor = 5.0;
    private const double Padding = 1.1;

    private readonly DataStreamer frontStreamer;
    private readonly DataStreamer rearStreamer;
    private double runningMax;

    public LiveTravelPlot(Plot plot, double travelMaximum)
        : base(plot, 2048, 0, Math.Max(1, travelMaximum))
    {
        ConfigurePlot("Travel", "Travel (mm)");
        frontStreamer = CreateStreamer(TelemetryPlot.FrontColor);
        rearStreamer = CreateStreamer(TelemetryPlot.RearColor);
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.FrontTravel.Count == 0 && batch.RearTravel.Count == 0)
        {
            return;
        }

        UpdateTiming(batch.TravelTimes);
        frontStreamer.AddRange(batch.FrontTravel);
        rearStreamer.AddRange(batch.RearTravel);
        UpdateRunningMax(batch.FrontTravel);
        UpdateRunningMax(batch.RearTravel);
        ApplyAutoLimits();
    }

    public override void Reset()
    {
        runningMax = 0;
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
