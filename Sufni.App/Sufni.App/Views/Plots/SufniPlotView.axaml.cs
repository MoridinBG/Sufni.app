using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ScottPlot.Avalonia;

namespace Sufni.App.Views.Plots;

public abstract class SufniPlotView : TemplatedControl
{
    public AvaPlot? AvaPlot;
    
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        AvaPlot = e.NameScope.Find<AvaPlot>("Plot");
        Debug.Assert(AvaPlot != null, nameof(AvaPlot) + " != null");
        
        CreatePlot();
    }

    protected abstract void CreatePlot();
}