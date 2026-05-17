using System;
using System.Diagnostics;
using Sufni.App.Plots;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.DesktopViews.Plots;

public sealed class LiveFramePitchRollPlotDesktopView : LiveGraphPlotDesktopViewBase
{
    protected override void CreatePlot()
    {
        Debug.Assert(HasPlotControl);
        Plot = new LiveFramePitchRollPlot(
            PlotControl.Plot,
            Math.Max(Math.Abs(MinimumY ?? -15), Math.Abs(MaximumY ?? 15)),
            HideRightAxis);
        ApplySmoothingLevel();
        ApplyConfiguredVerticalLimits();
        InitializeInteractions();
    }

    protected override void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ((LiveFramePitchRollPlot)Plot!).Append(batch);
    }
}
