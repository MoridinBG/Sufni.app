using System;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Plots;
using static Sufni.App.Tests.Infrastructure.TestTelemetryData;
using static Sufni.App.Tests.Infrastructure.PlotTestHelpers;

namespace Sufni.App.Tests.Plots;

public class TravelVelocityLegendTests
{
    [Fact]
    public void TravelPlot_LoadTelemetryData_ShowsLegend_WhenOnlyFrontSourceIsPresent()
    {
        var telemetry = CreateMinimal();
        telemetry.Rear.Present = false;
        var plot = new Plot();
        var sut = new TravelPlot(plot);

        sut.LoadTelemetryData(telemetry);

        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(
            ["Front"],
            plot.PlottableList.OfType<Signal>().Select(signal => signal.LegendText).ToArray());
    }

    [Fact]
    public void VelocityPlot_LoadTelemetryData_ShowsLegend_WhenOnlyRearSourceIsPresent()
    {
        var telemetry = CreateMinimal();
        telemetry.Front.Present = false;
        var plot = new Plot();
        var sut = new VelocityPlot(plot);

        sut.LoadTelemetryData(telemetry);

        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(
            ["Rear"],
            plot.PlottableList.OfType<Signal>().Select(signal => signal.LegendText).ToArray());
    }

    [Fact]
    public void TravelPlot_TryToggleInteractiveLegendAt_TogglesSourceAndKeepsLastSourceVisible()
    {
        var visibility = new TelemetrySourceVisibilityStore();
        var plot = new Plot();
        var sut = new TravelPlot(plot)
        {
            SourceVisibility = visibility,
        };
        sut.LoadTelemetryData(CreateMinimal());
        var front = Assert.Single(plot.PlottableList.OfType<Signal>(), signal => signal.LegendText == "Front");
        var rear = Assert.Single(plot.PlottableList.OfType<Signal>(), signal => signal.LegendText == "Rear");
        var plotSize = new PixelSize(500, 300);
        var initialLimits = plot.Axes.GetLimits();

        Assert.True(sut.TryToggleInteractiveLegendAt(GetLegendItemCenter(plot, rear, plotSize), plotSize));

        Assert.True(front.IsVisible);
        Assert.False(rear.IsVisible);
        Assert.False(visibility.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Rear));
        AssertAxisLimitsEqual(initialLimits, plot.Axes.GetLimits());

        Assert.False(sut.TryToggleInteractiveLegendAt(GetLegendItemCenter(plot, front, plotSize), plotSize));
        Assert.True(front.IsVisible);
        Assert.False(rear.IsVisible);
        Assert.True(visibility.IsVisible(TelemetryGraphRowIds.Travel, TelemetrySourceKeys.Front));
    }

    [Fact]
    public void TravelPlot_SourceVisibilityNull_DisablesInteractiveLegendAndRestoresSources()
    {
        var visibility = new TelemetrySourceVisibilityStore();
        var plot = new Plot();
        var sut = new TravelPlot(plot)
        {
            SourceVisibility = visibility,
        };
        sut.LoadTelemetryData(CreateMinimal());
        var rear = Assert.Single(plot.PlottableList.OfType<Signal>(), signal => signal.LegendText == "Rear");
        var plotSize = new PixelSize(500, 300);

        Assert.True(sut.TryToggleInteractiveLegendAt(GetLegendItemCenter(plot, rear, plotSize), plotSize));
        Assert.False(rear.IsVisible);

        sut.SourceVisibility = null;

        Assert.True(rear.IsVisible);
        Assert.False(plot.Legend.ShowItemsFromHiddenPlottables);
        Assert.False(sut.TryToggleInteractiveLegendAt(GetLegendItemCenter(plot, rear, plotSize), plotSize));
    }

}
