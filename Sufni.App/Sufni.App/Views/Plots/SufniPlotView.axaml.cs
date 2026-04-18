using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ScottPlot.Avalonia;

namespace Sufni.App.Views.Plots;

public abstract class SufniPlotView : TemplatedControl
{
    private AvaPlot? avaPlot;

    protected AvaPlot PlotControl => avaPlot!;
    protected bool HasPlotControl => avaPlot is not null;

    public void RefreshPlot() => avaPlot?.Refresh();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        avaPlot = e.NameScope.Find<AvaPlot>("Plot");
        Debug.Assert(avaPlot != null, nameof(avaPlot) + " != null");

        CreatePlot();
    }

    protected abstract void CreatePlot();
}