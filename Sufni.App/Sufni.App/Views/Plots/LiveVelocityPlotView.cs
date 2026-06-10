using System;
using System.Diagnostics;
using Sufni.App.Plots;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Views.Plots;

public sealed class LiveVelocityPlotView : LiveGraphPlotViewBase
{
    protected override void CreatePlot()
    {
        Debug.Assert(HasPlotControl);
        Plot = new LiveVelocityPlot(
            PlotControl.Plot,
            Math.Max(Math.Abs(MinimumY ?? 0), Math.Abs(MaximumY ?? 5)),
            HideRightAxis,
            CurrentTheme,
            SourceVisibility);
        ApplySmoothingLevel();
        ApplyConfiguredVerticalLimits();
        InitializeInteractions();
    }

    protected override void ApplyGraphBatch(LiveGraphBatch batch)
    {
        ((LiveVelocityPlot)Plot!).Append(batch);
    }
}
