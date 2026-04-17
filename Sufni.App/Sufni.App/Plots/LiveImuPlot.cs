using System;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Plots;

public sealed class LiveImuPlot : LiveStreamingPlotBase
{
    private readonly DataStreamer frameStreamer;
    private readonly DataStreamer forkStreamer;
    private readonly DataStreamer rearStreamer;

    public LiveImuPlot(Plot plot, double imuMaximum)
        : base(plot, 1024, 0, Math.Max(0.1, imuMaximum))
    {
        ConfigurePlot("IMU", "Acceleration (g)");
        frameStreamer = CreateStreamer(ImuPlot.FrameColor);
        forkStreamer = CreateStreamer(TelemetryPlot.FrontColor);
        rearStreamer = CreateStreamer(TelemetryPlot.RearColor);
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

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Frame, out var frameValues) && frameValues.Count > 0)
        {
            frameStreamer.AddRange(frameValues);
        }

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Fork, out var forkValues) && forkValues.Count > 0)
        {
            forkStreamer.AddRange(forkValues);
        }

        if (batch.ImuMagnitudes.TryGetValue(LiveImuLocation.Rear, out var rearValues) && rearValues.Count > 0)
        {
            rearStreamer.AddRange(rearValues);
        }
    }

    protected override void ClearStreamers()
    {
        frameStreamer.Clear(double.NaN);
        forkStreamer.Clear(double.NaN);
        rearStreamer.Clear(double.NaN);
    }
}
