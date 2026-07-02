using System;
using System.Diagnostics;

using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Plots;
namespace Sufni.App.LiveDaq.Views.Plots;

public sealed class LiveFramePitchRollPlotView : LiveSignalPlotViewBase
{
    protected override void CreatePlot()
    {
        Debug.Assert(HasPlotControl);
        Plot = new LiveFramePitchRollPlot(
            PlotControl.Plot,
            Math.Max(Math.Abs(MinimumY ?? -15), Math.Abs(MaximumY ?? 15)),
            HideRightAxis,
            CurrentTheme);
        ApplySmoothingLevel();
        ApplyConfiguredVerticalLimits();
        InitializeInteractions();
    }

    protected override void ApplySignalBatch(LiveSignalBatch batch)
    {
        ((LiveFramePitchRollPlot)Plot!).Append(batch);
    }
}
