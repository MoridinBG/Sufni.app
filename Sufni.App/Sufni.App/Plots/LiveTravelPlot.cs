using System;
using System.Collections.Generic;
using ScottPlot;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Theming;

namespace Sufni.App.Plots;

public sealed class LiveTravelPlot : LiveStreamingPlotBase
{
    private const double Floor = 5.0;
    private const double Padding = 1.1;
    private const int RenderCapacitySamples = 2048;
    private const int VisibleWindowDurationMilliseconds = 2048;

    private readonly LivePlotChannel frontChannel;
    private readonly LivePlotChannel rearChannel;
    private double runningMax;

    public LiveTravelPlot(Plot plot, double travelMaximum, bool hideRightAxis, SufniTheme? theme = null)
        : base(plot, "Travel (mm)", RenderCapacitySamples, VisibleWindowDurationMilliseconds, 0, Math.Max(1, travelMaximum), hideRightAxis, theme)
    {
        frontChannel = CreateChannel(TelemetryPlot.FrontColor, "Front");
        rearChannel = CreateChannel(TelemetryPlot.RearColor, "Rear");
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

        if (batch.FrontTravel.Count > 0)
        {
            frontChannel.Append(batch.TravelTimes, batch.FrontTravel, SmoothingLevel);
        }

        if (batch.RearTravel.Count > 0)
        {
            rearChannel.Append(batch.TravelTimes, batch.RearTravel, SmoothingLevel);
        }

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

    protected override IEnumerable<LivePlotChannel> Channels
    {
        get
        {
            yield return frontChannel;
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
