using System;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetryData;
using static Sufni.App.Tests.TestSupport.Fixtures.PlotTestHelpers;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Plots;
namespace Sufni.App.Tests.Sessions.Plots;

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
        Assert.False(visibility.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
        AssertAxisLimitsEqual(initialLimits, plot.Axes.GetLimits());

        Assert.False(sut.TryToggleInteractiveLegendAt(GetLegendItemCenter(plot, front, plotSize), plotSize));
        Assert.True(front.IsVisible);
        Assert.False(rear.IsVisible);
        Assert.True(visibility.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Front));
    }

    [Fact]
    public void TravelPlot_TryToggleInteractiveLegendAt_TogglesAllSegmentPlottablesForSource()
    {
        var telemetry = CreateMinimal();
        telemetry.Front.HasGaps = true;
        telemetry.Front.Segments =
        [
            new ProcessedSuspensionSegment { StartSeconds = 0.0, Travel = [0, 25], Velocity = [0, 10] },
            new ProcessedSuspensionSegment { StartSeconds = 1.0, Travel = [50, 75], Velocity = [20, 30] },
        ];
        telemetry.Rear.HasGaps = true;
        telemetry.Rear.Segments =
        [
            new ProcessedSuspensionSegment { StartSeconds = 0.0, Travel = [0, 20], Velocity = [0, 8] },
            new ProcessedSuspensionSegment { StartSeconds = 1.0, Travel = [40, 60], Velocity = [16, 24] },
        ];
        var visibility = new TelemetrySourceVisibilityStore();
        var plot = new Plot();
        var sut = new TravelPlot(plot)
        {
            SourceVisibility = visibility,
        };

        sut.LoadTelemetryData(telemetry);

        var scatters = plot.PlottableList.OfType<Scatter>().ToArray();
        Assert.Equal(4, scatters.Length);
        Assert.Equal(["Front", "", "Rear", ""], scatters.Select(scatter => scatter.LegendText ?? string.Empty).ToArray());
        var plotSize = new PixelSize(500, 300);

        Assert.True(sut.TryToggleInteractiveLegendAt(GetLegendItemCenter(plot, scatters[2], plotSize), plotSize));

        Assert.True(scatters[0].IsVisible);
        Assert.True(scatters[1].IsVisible);
        Assert.False(scatters[2].IsVisible);
        Assert.False(scatters[3].IsVisible);
        Assert.False(visibility.IsVisible(SignalRowIds.Travel, TelemetrySourceKeys.Rear));
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
