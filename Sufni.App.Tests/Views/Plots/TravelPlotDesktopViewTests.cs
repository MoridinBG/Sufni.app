using Avalonia.Headless.XUnit;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Plots;

public class TravelPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task TravelPlotDesktopView_StartsEmpty_BeforeTelemetryIsAssigned()
    {
        var view = new TravelPlotDesktopView();

        Assert.Null(view.MaximumDisplayHz);

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Empty(plot.Plot.PlottableList);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        var view = new TravelPlotDesktopView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal("Travel (mm / seconds)", plot.Plot.Axes.Title.Label.Text);
        Assert.Single(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_AnalysisRangeUpdatesOverlayWithoutReloadingSignals()
    {
        var view = new TravelPlotDesktopView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var originalSignals = plot.Plot.PlottableList.OfType<Signal>().ToArray();

        view.AnalysisRange = new TelemetryTimeRange(0.25, 0.75);
        await ViewTestHelpers.FlushDispatcherAsync();

        var updatedSignals = plot.Plot.PlottableList.OfType<Signal>().ToArray();
        var selectedSpan = Assert.Single(plot.Plot.PlottableList.OfType<HorizontalSpan>());
        Assert.Same(originalSignals[0], updatedSignals[0]);
        Assert.Same(originalSignals[1], updatedSignals[1]);
        Assert.Equal(0.25, selectedSpan.X1, 3);
        Assert.Equal(0.75, selectedSpan.X2, 3);

        view.AnalysisRange = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(selectedSpan.IsVisible);
    }

    [AvaloniaFact]
    public async Task TravelPlotDesktopView_RendersMarkerLinesFromTelemetry()
    {
        var view = new TravelPlotDesktopView();
        var telemetry = CreateTelemetryData();
        telemetry.Markers = [new MarkerData(0.5), new MarkerData(1.5)];

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        var markerLines = plot.Plot.PlottableList
            .OfType<VerticalLine>()
            .Where(line => !double.IsNaN(line.Position))
            .ToArray();
        Assert.Equal(2, markerLines.Length);
        Assert.Contains(markerLines, line => line.Position == 0.5);
        Assert.Contains(markerLines, line => line.Position == 1.5);
    }
}
