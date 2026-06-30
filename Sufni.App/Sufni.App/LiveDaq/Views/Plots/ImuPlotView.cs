
using Sufni.App.Shared.Views.Plots;
using Sufni.App.LiveDaq.Plots;
namespace Sufni.App.LiveDaq.Views.Plots;

public class ImuPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new ImuPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
