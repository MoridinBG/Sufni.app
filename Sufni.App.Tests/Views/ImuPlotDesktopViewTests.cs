using Avalonia.Headless.XUnit;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using Sufni.App.Tests.Views.Plots;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Plots;

public class ImuPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task ImuPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        var view = new ImuPlotDesktopView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryDataWithImu();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal("IMU Acceleration (g)", plot.Plot.Axes.Title.Label.Text);
        Assert.Single(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());
    }

    [AvaloniaFact]
    public async Task ImuPlotDesktopView_ShowsEmptyState_WhenTelemetryHasNoImuData()
    {
        var view = new ImuPlotDesktopView();

        await using var mounted = await PlotViewTestSupport.MountAsync(view);

        view.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mounted.View);
        Assert.Equal("IMU Acceleration (g)", plot.Plot.Axes.Title.Label.Text);
        Assert.Empty(plot.Plot.PlottableList.OfType<Signal>());
        Assert.Empty(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Single(plot.Plot.PlottableList.OfType<Text>());
    }

    [AvaloniaFact]
    public async Task ImuPlotDesktopView_RendersMarkerLinesFromTelemetry()
    {
        var view = new ImuPlotDesktopView();
        var telemetry = CreateTelemetryDataWithImu();
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
        Assert.All(markerLines, line =>
        {
            Assert.Equal(2.0f, line.LineWidth);
            Assert.Equal(Color.FromHex("#d53e4f").WithAlpha(0.9), line.LineColor);
        });
    }
}