using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Plots;

namespace Sufni.App.Tests.Plots;

public class RecordedTimeSeriesPlotTests
{
    [Fact]
    public void LoadTimeSeries_RendersSampledValuesAsSignal()
    {
        var plot = new Plot();
        var sut = new TestRecordedTimeSeriesPlot(plot);

        sut.LoadForTest(new RecordedTimeSeriesData(
            "Travel (mm)",
            "No travel data",
            DurationSeconds: 1.5,
            Series:
            [
                new RecordedTimeSeries(
                    "Front",
                    "mm",
                    TelemetryPlot.FrontColor,
                    new SampledValues([0, 25, 50, 75], SampleRate: 2),
                    "0.#")
            ]));

        var signal = Assert.Single(plot.PlottableList.OfType<Signal>());
        Assert.Empty(plot.Axes.Title.Label.Text);
        Assert.Same(plot.Axes.Bottom, signal.Axes.XAxis);
        Assert.Same(plot.Axes.Left, signal.Axes.YAxis);
        Assert.Equal("Front", signal.LegendText);
        Assert.False(plot.Legend.IsVisible);
        Assert.NotNull(sut.CursorLine);

        sut.SetCursorPositionWithReadout(1.0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Front: 50 mm", tooltip.LabelText);
    }

    [Fact]
    public void LoadTimeSeries_WithMultipleSeries_ShowsSourceLegend()
    {
        var plot = new Plot();
        var sut = new TestRecordedTimeSeriesPlot(plot);

        sut.LoadForTest(new RecordedTimeSeriesData(
            "Travel (mm)",
            "No travel data",
            DurationSeconds: 1.5,
            Series:
            [
                new RecordedTimeSeries(
                    "Front",
                    "mm",
                    TelemetryPlot.FrontColor,
                    new SampledValues([0, 25, 50, 75], SampleRate: 2),
                    "0.#"),
                new RecordedTimeSeries(
                    "Rear",
                    "mm",
                    TelemetryPlot.RearColor,
                    new SampledValues([5, 30, 55, 80], SampleRate: 2),
                    "0.#")
            ]));

        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(18, plot.Legend.SymbolHeight);
        Assert.Equal(
            ["Front", "Rear"],
            plot.PlottableList.OfType<Signal>().Select(signal => signal.LegendText).ToArray());
    }

    [Fact]
    public void LoadTimeSeries_RendersExplicitValuesAsScatterAndAppliesSmoothing()
    {
        var plot = new Plot();
        var sut = new TestRecordedTimeSeriesPlot(plot)
        {
            SmoothingLevel = PlotSmoothingLevel.Light,
        };

        sut.LoadForTest(new RecordedTimeSeriesData(
            "Speed (km/h)",
            "No speed data",
            DurationSeconds: 2,
            Series:
            [
                new RecordedTimeSeries(
                    "Speed",
                    "km/h",
                    Color.FromHex("#ffffbf"),
                    new ExplicitValues([0, 0.001, 0.002], [0, 9, 0]),
                    "0.#")
            ]));

        var scatter = Assert.Single(plot.PlottableList.OfType<Scatter>());
        Assert.False(scatter.MarkerStyle.IsVisible);
        Assert.Empty(plot.PlottableList.OfType<Signal>());
        Assert.NotNull(sut.CursorLine);

        sut.SetCursorPositionWithReadout(0.001);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 3 km/h", tooltip.LabelText);
    }

    private sealed class TestRecordedTimeSeriesPlot(Plot plot) : RecordedTimeSeriesPlot(plot)
    {
        public void LoadForTest(RecordedTimeSeriesData data)
        {
            LoadTimeSeries(data);
        }
    }
}
