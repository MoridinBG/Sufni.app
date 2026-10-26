using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Plots;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Plots;
using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
using static Sufni.App.Tests.TestSupport.Fixtures.TestSegmentComparisonRows;

namespace Sufni.App.Tests.Segments.Plots;

public class SegmentComparisonPlotTests
{
    [Fact]
    public void LoadRow_MapsRolesToSignalColors()
    {
        var plot = new Plot();

        new SegmentComparisonPlot(plot).LoadRow(Create(SegmentComparisonSignal.Imu,
        [
            Series(SegmentComparisonSeriesRole.FrontVelocity, 1, 2, 3),
            Series(SegmentComparisonSeriesRole.RearTravel, 4, 5, 6),
            Series(SegmentComparisonSeriesRole.FrameImu, 1, 2, 3),
            Series(SegmentComparisonSeriesRole.ForkImu, 1, 2, 3),
            Series(SegmentComparisonSeriesRole.ShockImu, 1, 2, 3),
            Series(SegmentComparisonSeriesRole.Speed, 1, 2, 3),
        ]));

        Assert.Equal(
            new[]
            {
                TelemetryPlot.FrontColor,
                TelemetryPlot.RearColor,
                ImuPlot.FrameColor,
                TelemetryPlot.FrontColor,
                TelemetryPlot.RearColor,
                SufniThemes.SignalSeries.GpsSpeed.ToScottPlotColor(),
            }.Select(Argb),
            plot.PlottableList.OfType<Scatter>().Select(scatter => Argb(scatter.Color)));
    }

    [Fact]
    public void LoadRow_InvertsTheValueAxis_ForTravel()
    {
        var plot = new Plot();

        new SegmentComparisonPlot(plot).LoadRow(
            Create(SegmentComparisonSignal.Travel, [Series(SegmentComparisonSeriesRole.FrontTravel, 10, 20, 15)]));

        var limits = plot.Axes.GetLimits();
        Assert.True(limits.Bottom > limits.Top);
    }

    [Fact]
    public void LoadRow_KeepsTheNaturalValueAxis_ForVelocity()
    {
        var plot = new Plot();

        new SegmentComparisonPlot(plot).LoadRow(
            Create(SegmentComparisonSignal.Velocity, [Series(SegmentComparisonSeriesRole.FrontVelocity, 10, 20, 15)]));

        var limits = plot.Axes.GetLimits();
        Assert.True(limits.Bottom < limits.Top);
    }

    [Fact]
    public void LoadRow_AddsAnAirtimeOverlay_WhenTheRowHasAirtimes()
    {
        var plot = new Plot();

        new SegmentComparisonPlot(plot).LoadRow(Create(
            SegmentComparisonSignal.Travel,
            [Series(SegmentComparisonSeriesRole.FrontTravel, 10, 20, 15)],
            [new SegmentMatchedAirtime(0.5, 1.0)]));

        Assert.NotEmpty(plot.PlottableList.OfType<HorizontalSpan>());
    }

    [Fact]
    public void LoadRow_ShowsTheEmptyMessage_WhenNoSeriesCanBeDrawn()
    {
        var plot = new Plot();

        new SegmentComparisonPlot(plot).LoadRow(
            Create(SegmentComparisonSignal.Velocity, [Series(SegmentComparisonSeriesRole.FrontVelocity, 42)]));

        Assert.Empty(plot.PlottableList.OfType<Scatter>());
        Assert.Contains(
            "No matched velocity data",
            plot.PlottableList.OfType<Text>().SelectMany(PlotTestHelpers.ReadTextLabels));
    }

    [Fact]
    public void LoadRow_SegmentedRuns_DoNotConnectOrShowCursorValuesAcrossTheGap()
    {
        var plot = new Plot();
        var sut = new SegmentComparisonPlot(plot)
        {
            MaximumDisplayHz = 30,
            SmoothingLevel = PlotSmoothingLevel.Strong,
        };
        SegmentComparisonSeriesRun[] runs =
        [
            new(Enumerable.Range(0, 11).Select(value => value / 100.0).ToArray(), Enumerable.Range(0, 11).Select(value => (double)value).ToArray()),
            new(Enumerable.Range(0, 11).Select(value => 1.0 + value / 100.0).ToArray(), Enumerable.Range(100, 11).Select(value => (double)value).ToArray()),
        ];

        sut.LoadRow(Create(
            SegmentComparisonSignal.Imu,
            [new SegmentComparisonSeries("Frame", "g", SegmentComparisonSeriesRole.FrameImu, [], [], "0.#", runs)]));

        Assert.Equal(2, plot.PlottableList.OfType<Scatter>().Count());
        sut.SetCursorPositionWithReadout(1.05);
        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Frame:", tooltip.LabelText);
        sut.SetCursorPositionWithReadout(0.6);
        Assert.False(tooltip.IsVisible);
    }

    [Fact]
    public void LoadRow_SegmentedRuns_UseAllRunsForTheValueLimits()
    {
        var plot = new Plot();

        new SegmentComparisonPlot(plot).LoadRow(Create(
            SegmentComparisonSignal.Imu,
            [
                new SegmentComparisonSeries(
                    "Frame",
                    "g",
                    SegmentComparisonSeriesRole.FrameImu,
                    [],
                    [],
                    "0.###",
                    [new([0, 0.1], [-10, -5]), new([1, 1.1], [100, 110])]),
            ]));

        var limits = plot.Axes.GetLimits();
        Assert.Equal(-19.6, limits.Bottom, precision: 6);
        Assert.Equal(119.6, limits.Top, precision: 6);
    }

    [Fact]
    public void LoadRow_ShowsTheLegend_OnlyForMultipleSeries()
    {
        var single = new Plot();
        var multiple = new Plot();

        new SegmentComparisonPlot(single).LoadRow(
            Create(SegmentComparisonSignal.Travel, [Series(SegmentComparisonSeriesRole.FrontTravel, 1, 2, 3)]));
        new SegmentComparisonPlot(multiple).LoadRow(Create(
            SegmentComparisonSignal.Travel,
            [Series(SegmentComparisonSeriesRole.FrontTravel, 1, 2, 3), Series(SegmentComparisonSeriesRole.RearTravel, 4, 5, 6)]));

        Assert.False(single.Legend.IsVisible);
        Assert.True(multiple.Legend.IsVisible);
    }

    private static (byte, byte, byte, byte) Argb(Color color) => (color.R, color.G, color.B, color.A);
}
