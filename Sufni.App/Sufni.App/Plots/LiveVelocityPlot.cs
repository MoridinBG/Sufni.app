using System;
using System.Collections.Generic;
using ScottPlot;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public sealed class LiveVelocityPlot : LiveStreamingPlotBase
{
    private const double Floor = 0.5;
    private const double Padding = 1.1;
    private const int RenderCapacitySamples = 2048;
    private const int VisibleWindowDurationMilliseconds = 2048;

    private readonly LivePlotChannel frontChannel;
    private readonly LivePlotChannel rearChannel;
    private double[] frontVelocityScratch = [];
    private double[] rearVelocityScratch = [];
    private double runningMaxAbs;

    public LiveVelocityPlot(Plot plot, double velocityMaximum, bool hideRightAxis, SufniTheme? theme = null)
        : base(plot, "Velocity (m/s)", RenderCapacitySamples, VisibleWindowDurationMilliseconds, -Math.Max(0.1, velocityMaximum), Math.Max(0.1, velocityMaximum), hideRightAxis, theme)
    {
        frontChannel = CreateChannel(TelemetryPlot.FrontColor, "Front");
        rearChannel = CreateChannel(TelemetryPlot.RearColor, "Rear");
        ShowSourceLegend();
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.FrontVelocity.Count == 0 && batch.RearVelocity.Count == 0)
        {
            return;
        }

        UpdateTiming(batch.VelocityTimes);

        if (batch.FrontVelocity.Count > 0)
        {
            var frontVelocity = ConvertToMetersPerSecond(batch.FrontVelocity, ref frontVelocityScratch);
            frontChannel.Append(batch.VelocityTimes, frontVelocity, SmoothingLevel);
            UpdateRunningMaxAbs(frontVelocity);
        }

        if (batch.RearVelocity.Count > 0)
        {
            var rearVelocity = ConvertToMetersPerSecond(batch.RearVelocity, ref rearVelocityScratch);
            rearChannel.Append(batch.VelocityTimes, rearVelocity, SmoothingLevel);
            UpdateRunningMaxAbs(rearVelocity);
        }

        ApplyAutoLimits();
    }

    public override void Reset()
    {
        runningMaxAbs = 0;
        ApplyAutoLimits();
        base.Reset();
    }

    protected override IEnumerable<LivePlotChannel> Channels
    {
        get
        {
            yield return frontChannel;
            yield return rearChannel;
        }
    }

    private static ArraySegment<double> ConvertToMetersPerSecond(IReadOnlyList<double> values, ref double[] buffer)
    {
        if (buffer.Length < values.Count)
        {
            buffer = new double[values.Count];
        }

        for (var index = 0; index < values.Count; index++)
        {
            buffer[index] = values[index] / 1000.0;
        }

        return new ArraySegment<double>(buffer, 0, values.Count);
    }

    private void UpdateRunningMaxAbs(ArraySegment<double> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var abs = Math.Abs(values.Array![values.Offset + i]);
            if (abs > runningMaxAbs)
            {
                runningMaxAbs = abs;
            }
        }
    }

    private void ApplyAutoLimits()
    {
        var max = Math.Max(runningMaxAbs * Padding, Floor);
        SetVerticalLimits(-max, max);
    }
}
