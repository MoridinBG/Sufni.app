using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public class ImuPlotDesktopView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new ImuPlot(PlotControl.Plot));
        InitializeCursorReadoutInteractions();
    }
}
