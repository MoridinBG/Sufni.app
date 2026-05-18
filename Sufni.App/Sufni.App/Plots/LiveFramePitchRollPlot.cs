using System;
using System.Collections.Generic;
using ScottPlot;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public sealed class LiveFramePitchRollPlot : LiveStreamingPlotBase
{
    private const double Floor = 5.0;
    private const double Padding = 1.1;
    private const int RenderCapacitySamples = 1024;
    private const int VisibleWindowDurationMilliseconds = 1024;

    private readonly LivePlotChannel pitchChannel;
    private readonly LivePlotChannel rollChannel;
    private double runningMaxAbs;

    public LiveFramePitchRollPlot(Plot plot, double pitchRollMaximum, bool hideRightAxis, SufniTheme? theme = null)
        : base(
            plot,
            "Frame Pitch/Roll (deg)",
            RenderCapacitySamples,
            VisibleWindowDurationMilliseconds,
            -Math.Max(Floor, pitchRollMaximum),
            Math.Max(Floor, pitchRollMaximum),
            hideRightAxis,
            theme)
    {
        pitchChannel = CreateChannel(TelemetryPlot.FrontColor, "Pitch");
        rollChannel = CreateChannel(TelemetryPlot.RearColor, "Roll");
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

        pitchChannel.Append(batch.FramePitchRollTimes, batch.FramePitchDegrees, SmoothingLevel);
        rollChannel.Append(batch.FramePitchRollTimes, batch.FrameRollDegrees, SmoothingLevel);
        UpdateRunningMaxAbs(batch.FramePitchDegrees);
        UpdateRunningMaxAbs(batch.FrameRollDegrees);
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
            yield return pitchChannel;
            yield return rollChannel;
        }
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
