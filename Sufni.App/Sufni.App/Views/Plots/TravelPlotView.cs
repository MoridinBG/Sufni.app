using Sufni.App.Plots;

namespace Sufni.App.Views.Plots;

public class TravelPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new TravelPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
