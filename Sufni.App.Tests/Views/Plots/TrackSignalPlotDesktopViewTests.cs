using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Models;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Plots;

public class TrackSignalPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task TrackSignalPlotDesktopView_ShowsRightAxisByDefault()
    {
        var view = CreateTrackSignalView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.True(plot.Plot.Axes.Right.IsVisible);
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

    [AvaloniaFact]
    public async Task TrackSignalPlotDesktopView_HidesRightAxis_WhenRequested()
    {
        var view = CreateTrackSignalView();
        view.HideRightAxis = true;

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.False(plot.Plot.Axes.Right.IsVisible);
    }

    [AvaloniaFact]
    public async Task TrackSignalPlotDesktopView_ReloadsTelemetryMarkersWhileHidden()
    {
        var view = CreateTrackSignalView();
        var oldTelemetry = CreateTelemetryData();
        oldTelemetry.Markers = [new MarkerData(0.5)];
        var freshTelemetry = CreateTelemetryData();
        freshTelemetry.Markers = [new MarkerData(0.25), new MarkerData(0.75)];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = oldTelemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal(2, plot.Plot.PlottableList.OfType<VerticalLine>().Count());

        view.IsVisible = false;
        view.Telemetry = freshTelemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(3, plot.Plot.PlottableList.OfType<VerticalLine>().Count());
    }

    private static TrackSignalPlotDesktopView CreateTrackSignalView() => new()
    {
        SignalKind = TrackSignalKind.Speed,
        TrackPoints =
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 1, 1, 101, 6),
        ],
        TimelineContext = new TrackTimeRange(0, 1),
    };
}
