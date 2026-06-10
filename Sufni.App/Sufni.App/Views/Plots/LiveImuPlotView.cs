using System.Diagnostics;
using Sufni.App.Plots;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Views.Plots;

public sealed class LiveImuPlotView : LiveGraphPlotViewBase
{
    protected override void CreatePlot()
    {
        Debug.Assert(HasPlotControl);
        Plot = new LiveImuPlot(PlotControl.Plot, MaximumY ?? 5, HideRightAxis, CurrentTheme, SourceVisibility);
        ApplySmoothingLevel();
        ApplyConfiguredVerticalLimits();
        InitializeInteractions();
    }

    protected override void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ((LiveImuPlot)Plot!).Append(batch);
    }
}
