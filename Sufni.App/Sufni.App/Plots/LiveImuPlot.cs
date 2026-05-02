using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Plots;

public sealed class LiveImuPlot : LiveStreamingPlotBase
{
    private const double Floor = 1.0;
    private const double Padding = 1.1;

    private readonly DataStreamer frameStreamer;
    private readonly DataStreamer forkStreamer;
    private readonly DataStreamer rearStreamer;
    private readonly TelemetryDisplayStreamingSmoother frameSmoother = new();
    private readonly TelemetryDisplayStreamingSmoother forkSmoother = new();
    private readonly TelemetryDisplayStreamingSmoother rearSmoother = new();
    private double[] frameSmoothingScratch = [];
    private double[] forkSmoothingScratch = [];
    private double[] rearSmoothingScratch = [];
    private double runningMax;

    public LiveImuPlot(Plot plot, double imuMaximum)
        : base(plot, 1024, 0, Math.Max(0.1, imuMaximum))
    {
        ConfigurePlot("IMU", "Acceleration (g)");
        frameStreamer = CreateStreamer(ImuPlot.FrameColor);
        forkStreamer = CreateStreamer(TelemetryPlot.FrontColor);
        rearStreamer = CreateStreamer(TelemetryPlot.RearColor);
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.ImuMagnitudes.Count == 0)
        {
            return;
        }

        if (batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var frameTimes) && frameTimes.Count > 0)
        {
            UpdateTiming(frameTimes);
        }
        else if (batch.ImuTimes.TryGetValue(LiveImuLocation.Fork, out var forkTimes) && forkTimes.Count > 0)
        {
            UpdateTiming(forkTimes);
        }
        else if (batch.ImuTimes.TryGetValue(LiveImuLocation.Rear, out var rearTimes) && rearTimes.Count > 0)
        {
            UpdateTiming(rearTimes);
        }

        frameSmoother.Level = SmoothingLevel;
        forkSmoother.Level = SmoothingLevel;
        rearSmoother.Level = SmoothingLevel;

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Frame, out var frameValues) && frameValues.Count > 0)
        {
            frameStreamer.AddRange(frameSmoother.Apply(frameValues, ref frameSmoothingScratch));
            UpdateRunningMax(frameValues);
        }

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Fork, out var forkValues) && forkValues.Count > 0)
        {
            forkStreamer.AddRange(forkSmoother.Apply(forkValues, ref forkSmoothingScratch));
            UpdateRunningMax(forkValues);
        }

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Rear, out var rearValues) && rearValues.Count > 0)
        {
            rearStreamer.AddRange(rearSmoother.Apply(rearValues, ref rearSmoothingScratch));
            UpdateRunningMax(rearValues);
        }

        ApplyAutoLimits();
    }

    public override void Reset()
    {
        runningMax = 0;
        frameSmoother.Reset();
        forkSmoother.Reset();
        rearSmoother.Reset();
        ApplyAutoLimits();
        base.Reset();
    }

    protected override void ClearStreamers()
    {
        frameStreamer.Clear(double.NaN);
        forkStreamer.Clear(double.NaN);
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
