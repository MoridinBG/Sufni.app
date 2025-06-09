using ScottPlot;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

internal class LockedVerticalSoftLockedHorizontalRule(IXAxis xAxis, IYAxis yAxis, double xMin, double xMax, double yMin, double yMax) : IAxisRule
{
    public void Apply(RenderPack rp, bool beforeLayout)
    {
        if (xAxis.Min < xMin) xAxis.Min = xMin;
        if (xAxis.Max > xMax) xAxis.Max = xMax;
        yAxis.Range.Set(yMin, yMax);
    }
}

public class TelemetryPlot(Plot plot) : SufniPlot(plot)
{
    protected Color FrontColor = Color.FromHex("#3288bd");
    protected Color RearColor = Color.FromHex("#66c2a5");

    public virtual void LoadTelemetryData(TelemetryData telemetryData) { }
}