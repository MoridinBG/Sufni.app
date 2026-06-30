using System.Diagnostics;

using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Plots;
namespace Sufni.App.LiveDaq.Views.Plots;

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
