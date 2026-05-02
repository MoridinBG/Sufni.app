using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Plots;

public sealed class LiveVelocityPlot : LiveStreamingPlotBase
{
    private const double Floor = 0.5;
    private const double Padding = 1.1;

    private readonly DataStreamer frontStreamer;
    private readonly DataStreamer rearStreamer;
    private readonly TelemetryDisplayStreamingSmoother frontSmoother = new();
    private readonly TelemetryDisplayStreamingSmoother rearSmoother = new();
    private double[] frontVelocityScratch = [];
    private double[] rearVelocityScratch = [];
    private double[] frontSmoothingScratch = [];
    private double[] rearSmoothingScratch = [];
    private double runningMaxAbs;

    public LiveVelocityPlot(Plot plot, double velocityMaximum)
        : base(plot, 2048, -Math.Max(0.1, velocityMaximum), Math.Max(0.1, velocityMaximum))
    {
        ConfigurePlot("Velocity", "Velocity (m/s)");
        frontStreamer = CreateStreamer(TelemetryPlot.FrontColor);
        rearStreamer = CreateStreamer(TelemetryPlot.RearColor);
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.FrontVelocity.Count == 0 && batch.RearVelocity.Count == 0)
        {
            return;
        }

        UpdateTiming(batch.VelocityTimes);
        frontSmoother.Level = SmoothingLevel;
        rearSmoother.Level = SmoothingLevel;

        if (batch.FrontVelocity.Count > 0)
        {
            var frontVelocity = ConvertToMetersPerSecond(batch.FrontVelocity, ref frontVelocityScratch);
            frontStreamer.AddRange(frontSmoother.Apply(frontVelocity, ref frontSmoothingScratch));
            UpdateRunningMaxAbs(frontVelocity);
        }

        if (batch.RearVelocity.Count > 0)
        {
            var rearVelocity = ConvertToMetersPerSecond(batch.RearVelocity, ref rearVelocityScratch);
            rearStreamer.AddRange(rearSmoother.Apply(rearVelocity, ref rearSmoothingScratch));
            UpdateRunningMaxAbs(rearVelocity);
        }

        ApplyAutoLimits();
    }

    public override void Reset()
    {
        runningMaxAbs = 0;
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
