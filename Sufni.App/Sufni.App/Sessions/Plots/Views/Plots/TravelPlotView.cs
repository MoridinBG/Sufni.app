
using Sufni.App.Shared.Views.Plots;
namespace Sufni.App.Sessions.Plots.Views.Plots;

public class TravelPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new TravelPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
