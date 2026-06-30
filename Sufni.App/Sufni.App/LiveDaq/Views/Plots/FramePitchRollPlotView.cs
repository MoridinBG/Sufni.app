
using Sufni.App.Shared.Views.Plots;
using Sufni.App.LiveDaq.Plots;
namespace Sufni.App.LiveDaq.Views.Plots;

public class FramePitchRollPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new FramePitchRollPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
