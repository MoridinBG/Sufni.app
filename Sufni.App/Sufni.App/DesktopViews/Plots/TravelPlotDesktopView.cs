using System.Diagnostics;
using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public class TravelPlotDesktopView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot != null, nameof(AvaPlot) + " != null");
        Plot = new TravelPlot(AvaPlot.Plot);
    }
}