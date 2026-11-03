using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ScottPlot.Avalonia;

namespace Sufni.App.Tests.TestSupport.Harness;

internal static class PlotViewTestSupport
{
    public static AvaPlot GetRenderedPlot(Control view) =>
        Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());

    public static async Task<MountedView<TView>> MountAsync<TView>(TView view)
        where TView : Control
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        return await ViewTestHelpers.MountAsync(view);
    }
}
