using Sufni.App.Plots;

namespace Sufni.App.Views.Plots;

public class FramePitchRollPlotView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new FramePitchRollPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
