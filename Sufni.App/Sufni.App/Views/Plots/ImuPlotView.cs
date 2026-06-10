using Sufni.App.Plots;

namespace Sufni.App.Views.Plots;

public class ImuPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new ImuPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
