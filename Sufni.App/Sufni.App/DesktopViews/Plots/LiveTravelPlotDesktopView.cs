using System.Diagnostics;
using Sufni.App.Plots;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.DesktopViews.Plots;

public sealed class LiveTravelPlotDesktopView : LiveGraphPlotDesktopViewBase
{
    protected override void CreatePlot()
    {
        Debug.Assert(AvaPlot is not null);
        Plot = new LiveTravelPlot(AvaPlot.Plot, MaximumY ?? 1);
        ApplyConfiguredVerticalLimits();
        InitializeInteractions();
    }

    protected override void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ((LiveTravelPlot)Plot!).Append(batch);
    }
}
