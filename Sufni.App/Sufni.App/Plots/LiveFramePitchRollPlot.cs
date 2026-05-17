using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Plots;

public sealed class LiveFramePitchRollPlot : LiveStreamingPlotBase
{
    private const double Floor = 5.0;
    private const double Padding = 1.1;
    private const int RenderCapacitySamples = 1024;
    private const int VisibleWindowDurationMilliseconds = 1024;

    private readonly DataStreamer pitchStreamer;
    private readonly DataStreamer rollStreamer;
    private readonly TelemetryDisplayStreamingSmoother pitchSmoother = new();
    private readonly TelemetryDisplayStreamingSmoother rollSmoother = new();
    private double[] pitchSmoothingScratch = [];
    private double[] rollSmoothingScratch = [];
    private double runningMaxAbs;

    public LiveFramePitchRollPlot(Plot plot, double pitchRollMaximum, bool hideRightAxis)
        : base(
            plot,
            "Frame Pitch/Roll (deg)",
            RenderCapacitySamples,
            VisibleWindowDurationMilliseconds,
            -Math.Max(Floor, pitchRollMaximum),
            Math.Max(Floor, pitchRollMaximum),
            hideRightAxis)
    {
        pitchStreamer = CreateStreamer(TelemetryPlot.FrontColor, "Pitch");
        rollStreamer = CreateStreamer(TelemetryPlot.RearColor, "Roll");
        ShowSourceLegend();
        ApplyAutoLimits();
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.FramePitchRollTimes.Count == 0 ||
            batch.FramePitchDegrees.Count == 0 ||
            batch.FrameRollDegrees.Count == 0)
        {
            return;
        }

        UpdateTiming(batch.FramePitchRollTimes);
        pitchSmoother.Level = SmoothingLevel;
        rollSmoother.Level = SmoothingLevel;

        pitchStreamer.AddRange(pitchSmoother.Apply(batch.FramePitchRollTimes, batch.FramePitchDegrees, ref pitchSmoothingScratch));
        rollStreamer.AddRange(rollSmoother.Apply(batch.FramePitchRollTimes, batch.FrameRollDegrees, ref rollSmoothingScratch));
        UpdateRunningMaxAbs(batch.FramePitchDegrees);
        UpdateRunningMaxAbs(batch.FrameRollDegrees);
        ApplyAutoLimits();
    }

    public override void Reset()
    {
        runningMaxAbs = 0;
        pitchSmoother.Reset();
        rollSmoother.Reset();
        ApplyAutoLimits();
        base.Reset();
    }

    protected override void ClearStreamers()
    {
        pitchStreamer.Clear(double.NaN);
        rollStreamer.Clear(double.NaN);
    }

    private void UpdateRunningMaxAbs(IReadOnlyList<double> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var abs = Math.Abs(values[i]);
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
