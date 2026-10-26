using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Views;
using Sufni.App.Tests.TestSupport.Harness;
using static Sufni.App.Tests.TestSupport.Fixtures.TestSegmentComparisonRows;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class SegmentComparisonPlotViewTests
{
    [AvaloniaFact]
    public async Task RendersTheRowSeries()
    {
        var view = new SegmentComparisonPlotView
        {
            DataContext = Create(SegmentComparisonSignal.Travel, [Series(SegmentComparisonSeriesRole.FrontTravel, 10, 20, 15)]),
        };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Single(plot.Plot.GetPlottables().OfType<Scatter>());
    }

    [AvaloniaFact]
    public async Task ShowAirtime_FollowsTheRowToggle()
    {
        var row = Create(
            SegmentComparisonSignal.Travel,
            [Series(SegmentComparisonSeriesRole.FrontTravel, 10, 20, 15)],
            [new SegmentMatchedAirtime(1, 2)]);
        var view = new SegmentComparisonPlotView { DataContext = row };

        await using var mounted = await PlotViewTestSupport.MountAsync(view);
        Assert.True(view.ShowAirtime);

        row.HeaderActions.Single(action => action.Id == "airtime").Command!.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(view.ShowAirtime);
    }
}
