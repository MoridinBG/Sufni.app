
using Sufni.App.Shared.Views.Plots;
namespace Sufni.App.Sessions.Plots.Views.Plots;

public class VelocityPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new VelocityPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
