using System;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Plots;
using static Sufni.App.Tests.Infrastructure.TestTelemetryFactories;

namespace Sufni.App.Tests.Plots;

public class TravelVelocityLegendTests
{
    [Fact]
    public void TravelPlot_LoadTelemetryData_ShowsLegend_WhenOnlyFrontSourceIsPresent()
    {
        var telemetry = CreateTelemetryData();
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
        var telemetry = CreateTelemetryData();
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
        sut.LoadTelemetryData(CreateTelemetryData());
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

    private static Pixel GetLegendItemCenter(Plot plot, IPlottable plottable, PixelSize plotSize)
    {
        plot.RenderInMemory((int)plotSize.Width, (int)plotSize.Height);
        using var paint = Paint.NewDisposablePaint();
        var dataRect = plot.LastRender.DataRect.HasArea
            ? plot.LastRender.DataRect
            : plotSize.ToPixelRect();
        var layout = plot.Legend.GetLayout(dataRect.Size, paint);
        var legendRect = layout.LegendRect.AlignedInside(dataRect, plot.Legend.Alignment);
        var itemCount = layout.LegendItems.Length;

        for (var index = 0; index < itemCount; index++)
        {
            if (!ReferenceEquals(layout.LegendItems[index].Plottable, plottable))
            {
                continue;
            }

            var rowHeight = legendRect.Height / itemCount;
            return new Pixel(
                (legendRect.Left + legendRect.Right) / 2,
                legendRect.Top + rowHeight * index + rowHeight / 2);
        }

        throw new InvalidOperationException("Legend item was not found.");
    }

    private static void AssertAxisLimitsEqual(AxisLimits expected, AxisLimits actual)
    {
        Assert.Equal(expected.Left, actual.Left, precision: 8);
        Assert.Equal(expected.Right, actual.Right, precision: 8);
        Assert.Equal(expected.Bottom, actual.Bottom, precision: 8);
        Assert.Equal(expected.Top, actual.Top, precision: 8);
    }
}
