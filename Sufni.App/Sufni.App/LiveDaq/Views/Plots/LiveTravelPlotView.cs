using System.Diagnostics;

using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Plots;
namespace Sufni.App.LiveDaq.Views.Plots;

public sealed class LiveTravelPlotView : LiveGraphPlotViewBase
{
    protected override void CreatePlot()
    {
        Debug.Assert(HasPlotControl);
        Plot = new LiveTravelPlot(PlotControl.Plot, MaximumY ?? 1, HideRightAxis, CurrentTheme, SourceVisibility);
        ApplySmoothingLevel();
        ApplyConfiguredVerticalLimits();
        InitializeInteractions();
    }

    protected override void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ((LiveTravelPlot)Plot!).Append(batch);
    }
}
