using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Plots;

public sealed class LiveVelocityPlot : LiveStreamingPlotBase
{
    private readonly DataStreamer frontStreamer;
    private readonly DataStreamer rearStreamer;

    public LiveVelocityPlot(Plot plot)
        : base(plot, 2048)
    {
        ConfigurePlot("Velocity", "Velocity (m/s)");
        frontStreamer = CreateStreamer(Color.FromHex("#3288bd"));
        rearStreamer = CreateStreamer(Color.FromHex("#66c2a5"));
    }

    public void Append(LiveGraphBatch batch)
    {
        if (batch.FrontVelocity.Count == 0 && batch.RearVelocity.Count == 0)
        {
            return;
        }

        UpdateTiming(batch.VelocityTimes);
        frontStreamer.AddRange(batch.FrontVelocity.Select(value => value / 1000.0));
        rearStreamer.AddRange(batch.RearVelocity.Select(value => value / 1000.0));
        UpdateVerticalLimits(GetFiniteValues(frontStreamer), GetFiniteValues(rearStreamer));
    }

    protected override void ClearStreamers()
    {
        frontStreamer.Clear(double.NaN);
        rearStreamer.Clear(double.NaN);
    }
}
