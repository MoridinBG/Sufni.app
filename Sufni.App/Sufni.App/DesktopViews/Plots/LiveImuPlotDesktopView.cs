using System.Diagnostics;
using Sufni.App.Plots;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.DesktopViews.Plots;

public sealed class LiveImuPlotDesktopView : LiveGraphPlotDesktopViewBase
{
    protected override void CreatePlot()
    {
        Debug.Assert(HasPlotControl);
        Plot = new LiveImuPlot(PlotControl.Plot, MaximumY ?? 5);
        ApplyConfiguredVerticalLimits();
        InitializeInteractions();
    }

    protected override void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ((LiveImuPlot)Plot!).Append(batch);
    }
}
