using System;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Plots;

public sealed class LiveTravelPlot : LiveStreamingPlotBase
{
    private readonly DataStreamer frontStreamer;
    private readonly DataStreamer rearStreamer;

    public LiveTravelPlot(Plot plot, double travelMaximum)
        : base(plot, 2048, 0, Math.Max(1, travelMaximum))
    {
        ConfigurePlot("Travel", "Travel (mm)");
        frontStreamer = CreateStreamer(Color.FromHex("#3288bd"));
        rearStreamer = CreateStreamer(Color.FromHex("#66c2a5"));
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
    }

    protected override void ClearStreamers()
    {
        frontStreamer.Clear(double.NaN);
        rearStreamer.Clear(double.NaN);
    }
}
