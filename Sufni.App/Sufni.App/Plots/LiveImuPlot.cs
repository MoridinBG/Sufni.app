using System;
using System.Collections.Generic;
using ScottPlot;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public sealed class LiveImuPlot : LiveStreamingPlotBase
{
    private const double Floor = 1.0;
    private const double Padding = 1.1;
    private const int RenderCapacitySamples = 1024;
    private const int VisibleWindowDurationMilliseconds = 1024;

    private readonly LivePlotChannel frameChannel;
    private readonly LivePlotChannel forkChannel;
    private readonly LivePlotChannel rearChannel;
    private double runningMax;

    public LiveImuPlot(Plot plot, double imuMaximum, bool hideRightAxis, SufniTheme? theme = null)
        : base(plot, "Vibration RMS (g)", RenderCapacitySamples, VisibleWindowDurationMilliseconds, 0, Math.Max(0.1, imuMaximum), hideRightAxis, theme)
    {
        frameChannel = CreateChannel(ImuPlot.FrameColor, "Frame");
        forkChannel = CreateChannel(TelemetryPlot.FrontColor, "Fork");
        rearChannel = CreateChannel(TelemetryPlot.RearColor, "Shock");
        ShowSourceLegend();
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.ImuVibrationRms.Count == 0)
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

        if (batch.ImuVibrationRms.TryGetValue(LiveImuLocation.Frame, out var frameValues) &&
            batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var frameTimes) &&
            frameValues.Count > 0)
        {
            frameChannel.Append(frameTimes, frameValues, SmoothingLevel);
            UpdateRunningMax(frameValues);
        }

        if (batch.ImuVibrationRms.TryGetValue(LiveImuLocation.Fork, out var forkValues) &&
            batch.ImuTimes.TryGetValue(LiveImuLocation.Fork, out var forkTimes) &&
            forkValues.Count > 0)
        {
            forkChannel.Append(forkTimes, forkValues, SmoothingLevel);
            UpdateRunningMax(forkValues);
        }

        if (batch.ImuVibrationRms.TryGetValue(LiveImuLocation.Rear, out var rearValues) &&
            batch.ImuTimes.TryGetValue(LiveImuLocation.Rear, out var rearTimes) &&
            rearValues.Count > 0)
        {
            rearChannel.Append(rearTimes, rearValues, SmoothingLevel);
            UpdateRunningMax(rearValues);
        }

        ApplyAutoLimits();
    }

    public override void Reset()
    {
        runningMax = 0;
        ApplyAutoLimits();
        base.Reset();
    }

    protected override IEnumerable<LivePlotChannel> Channels
    {
        get
        {
            yield return frameChannel;
            yield return forkChannel;
            yield return rearChannel;
        }
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
