using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public class VelocityPlotDesktopView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new VelocityPlot(PlotControl.Plot));
        InitializeCursorReadoutInteractions();
    }
}
