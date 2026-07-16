using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shared.Views.Plots;

[Collection("Ui")]
public class SufniPlotViewInvalidationTests
{
    [AvaloniaFact]
    public async Task RefreshPlot_CoalescesPendingReasons_IntoOneFlush()
    {
        var (view, host) = await MountAsync();
        try
        {
            view.FlushedInvalidations.Clear();

            view.RefreshPlot(PlotInvalidation.Cursor);
            view.RefreshPlot(PlotInvalidation.Data);
            view.RefreshPlot(PlotInvalidation.Cursor);

            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(
                [PlotInvalidation.Cursor | PlotInvalidation.Data],
                view.FlushedInvalidations);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task RefreshPlot_DoesNotFlush_None()
    {
        var (view, host) = await MountAsync();
        try
        {
            view.FlushedInvalidations.Clear();

            view.RefreshPlot(PlotInvalidation.None);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Empty(view.FlushedInvalidations);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private static async Task<(TrackingPlotView View, Window Host)> MountAsync()
    {
        ViewTestHelpers.EnsurePlotViewStyle();
        var view = new TrackingPlotView();
        var host = new Window { Content = view };
        host.Show();
        await ViewTestHelpers.FlushDispatcherAsync();
        return (view, host);
    }

    private sealed class TrackingPlotView : SufniPlotView
    {
        public List<PlotInvalidation> FlushedInvalidations { get; } = [];

        protected override void CreatePlot()
        {
        }

        protected override void RefreshPlotCore(PlotInvalidation invalidation)
        {
            FlushedInvalidations.Add(invalidation);
            base.RefreshPlotCore(invalidation);
        }
    }
}
