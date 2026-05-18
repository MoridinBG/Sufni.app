using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public sealed class LiveImuPlot : LiveStreamingPlotBase
{
    private const double Floor = 1.0;
    private const double Padding = 1.1;
    private const int RenderCapacitySamples = 1024;
    private const int VisibleWindowDurationMilliseconds = 1024;

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

    public LiveImuPlot(Plot plot, double imuMaximum, bool hideRightAxis, SufniTheme? theme = null)
        : base(plot, "IMU Acceleration (g)", RenderCapacitySamples, VisibleWindowDurationMilliseconds, 0, Math.Max(0.1, imuMaximum), hideRightAxis, theme)
    {
        frameStreamer = CreateStreamer(ImuPlot.FrameColor, "Frame");
        forkStreamer = CreateStreamer(TelemetryPlot.FrontColor, "Fork");
        rearStreamer = CreateStreamer(TelemetryPlot.RearColor, "Shock");
        ShowSourceLegend();
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.ImuMagnitudes.Count == 0)
        {
            return;
        }

        if (batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var timingFrameTimes) && timingFrameTimes.Count > 0)
        {
            UpdateTiming(timingFrameTimes);
        }
        else if (batch.ImuTimes.TryGetValue(LiveImuLocation.Fork, out var timingForkTimes) && timingForkTimes.Count > 0)
        {
            UpdateTiming(timingForkTimes);
        }
        else if (batch.ImuTimes.TryGetValue(LiveImuLocation.Rear, out var timingRearTimes) && timingRearTimes.Count > 0)
        {
            UpdateTiming(timingRearTimes);
        }

        frameSmoother.Level = SmoothingLevel;
        forkSmoother.Level = SmoothingLevel;
        rearSmoother.Level = SmoothingLevel;

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Frame, out var frameValues) &&
            batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var frameTimes) &&
            frameValues.Count > 0)
        {
            frameStreamer.AddRange(frameSmoother.Apply(frameTimes, frameValues, ref frameSmoothingScratch));
            UpdateRunningMax(frameValues);
        }

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Fork, out var forkValues) &&
            batch.ImuTimes.TryGetValue(LiveImuLocation.Fork, out var forkTimes) &&
            forkValues.Count > 0)
        {
            forkStreamer.AddRange(forkSmoother.Apply(forkTimes, forkValues, ref forkSmoothingScratch));
            UpdateRunningMax(forkValues);
        }

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Rear, out var rearValues) &&
            batch.ImuTimes.TryGetValue(LiveImuLocation.Rear, out var rearTimes) &&
            rearValues.Count > 0)
        {
            rearStreamer.AddRange(rearSmoother.Apply(rearTimes, rearValues, ref rearSmoothingScratch));
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
