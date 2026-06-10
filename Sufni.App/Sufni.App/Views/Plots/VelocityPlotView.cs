using Sufni.App.Plots;

namespace Sufni.App.Views.Plots;

public class VelocityPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new VelocityPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
