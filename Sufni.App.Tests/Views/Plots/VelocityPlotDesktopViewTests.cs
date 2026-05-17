using Avalonia.Headless.XUnit;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Views.Plots;

public class VelocityPlotDesktopViewTests
{
    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_StartsEmpty_BeforeTelemetryIsAssigned()
    {
        var velocityView = new VelocityPlotDesktopView();

        await using var mountedVelocity = await PlotViewTestSupport.MountAsync(velocityView);

        var plot = PlotViewTestSupport.GetRenderedPlot(mountedVelocity.View);
        Assert.Empty(plot.Plot.PlottableList);
    }

    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_LoadsSignalsFromTelemetryProperty()
    {
        var velocityView = new VelocityPlotDesktopView();

        await using var mountedVelocity = await PlotViewTestSupport.MountAsync(velocityView);

        velocityView.Telemetry = CreateTelemetryData();
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mountedVelocity.View);
        Assert.Empty(plot.Plot.Axes.Title.Label.Text);
        Assert.Single(plot.Plot.PlottableList.OfType<VerticalLine>());
        Assert.Equal(2, plot.Plot.PlottableList.OfType<Signal>().Count());
        Assert.True(plot.Plot.Axes.Right.IsVisible);
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_ShowsEmptyState_WhenTelemetryHasNoVelocityData()
    {
        var velocityView = new VelocityPlotDesktopView();

        await using var mountedVelocity = await PlotViewTestSupport.MountAsync(velocityView);

        var telemetry = CreateTelemetryData();
        telemetry.Front.Present = false;
        telemetry.Rear.Present = false;

        velocityView.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mountedVelocity.View);
        Assert.Empty(plot.Plot.Axes.Title.Label.Text);
        Assert.Empty(plot.Plot.PlottableList.OfType<Signal>());
        Assert.Single(plot.Plot.PlottableList.OfType<Text>());
        Assert.Equal(plot.Plot.Axes.Left.Min, plot.Plot.Axes.Right.Min, precision: 6);
        Assert.Equal(plot.Plot.Axes.Left.Max, plot.Plot.Axes.Right.Max, precision: 6);
    }

    [AvaloniaFact]
    public async Task VelocityPlotDesktopView_RendersMarkerLinesFromTelemetry()
    {
        var velocityView = new VelocityPlotDesktopView();

        await using var mountedVelocity = await PlotViewTestSupport.MountAsync(velocityView);

        var telemetry = CreateTelemetryData();
        telemetry.Markers = [new MarkerData(0.5), new MarkerData(1.5)];

        velocityView.Telemetry = telemetry;
        await ViewTestHelpers.FlushDispatcherAsync();

        var plot = PlotViewTestSupport.GetRenderedPlot(mountedVelocity.View);
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
