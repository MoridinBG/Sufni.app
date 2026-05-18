using Sufni.App.Plots;

namespace Sufni.App.DesktopViews.Plots;

public class FramePitchRollPlotDesktopView : SufniTelemetryPlotView
{
    protected override void CreatePlot()
    {
        SetPlotModel(new FramePitchRollPlot(PlotControl.Plot, CurrentTheme));
        InitializeCursorReadoutInteractions();
    }
}
