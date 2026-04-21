using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ScottPlot.Avalonia;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Views.Plots;

internal static class PlotViewTestSupport
{
    public static AvaPlot GetRenderedPlot(Control view) =>
        Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());

    public static async Task<MountedPlotView<TView>> MountAsync<TView>(TView view)
        where TView : Control
    {
        ViewTestHelpers.EnsurePlotViewStyle();

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedPlotView<TView>(host, view);
    }
}

internal sealed class MountedPlotView<TView>(Window host, TView view) : IAsyncDisposable
    where TView : Control
{
    public Window Host { get; } = host;
    public TView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}