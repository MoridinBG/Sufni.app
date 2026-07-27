using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Theming;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Extensibility.Views;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Sessions.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Extensibility.Views;

public class ExtensionSignalPlotTests
{
    [Fact]
    public void LoadSeries_MapsRolesToThemeInvariantSignalColors()
    {
        var plot = new Plot();
        var sut = new ExtensionSignalPlot(plot);

        sut.LoadSeries(CreateViewModel(
        [
            Series(RecordedSessionSignalSeriesRole.FrontSuspension, 1, 2, 3),
            Series(RecordedSessionSignalSeriesRole.RearSuspension, 4, 5, 6),
            Series(RecordedSessionSignalSeriesRole.FrameImu, 1, 2, 3),
            Series(RecordedSessionSignalSeriesRole.ForkImu, 1, 2, 3),
            Series(RecordedSessionSignalSeriesRole.ShockImu, 1, 2, 3),
            Series(RecordedSessionSignalSeriesRole.GpsSpeed, 1, 2, 3),
        ]));

        var actual = plot.PlottableList.OfType<Scatter>().Select(scatter => Argb(scatter.Color));
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
            actual);
    }

    [Fact]
    public void LoadSeries_InvertsValueAxis_ForTravel()
    {
        var plot = new Plot();
        new ExtensionSignalPlot(plot).LoadSeries(CreateViewModel(
            [Series(RecordedSessionSignalSeriesRole.FrontSuspension, 10, 20, 15)],
            invertValueAxis: true));

        var limits = plot.Axes.GetLimits();
        Assert.True(limits.Bottom > limits.Top, "Travel value axis should be inverted (0 at top).");
    }

    [Fact]
    public void LoadSeries_KeepsNaturalValueAxis_WhenNotInverted()
    {
        var plot = new Plot();
        new ExtensionSignalPlot(plot).LoadSeries(CreateViewModel(
            [Series(RecordedSessionSignalSeriesRole.FrontSuspension, 10, 20, 15)],
            invertValueAxis: false));

        var limits = plot.Axes.GetLimits();
        Assert.True(limits.Bottom < limits.Top);
    }

    [Fact]
    public void LoadSeries_AddsAirtimeOverlay_WhenSpansPresent()
    {
        var plot = new Plot();
        new ExtensionSignalPlot(plot).LoadSeries(CreateViewModel(
            [Series(RecordedSessionSignalSeriesRole.FrontSuspension, 10, 20, 15)],
            invertValueAxis: true,
            airtimeSpans: [new RecordedSessionSignalSpan(0.5, 1.0)]));

        Assert.NotEmpty(plot.PlottableList.OfType<HorizontalSpan>());
    }

    [Fact]
    public void LoadSeries_AddsNoAirtimeOverlay_WhenNoSpans()
    {
        var plot = new Plot();
        new ExtensionSignalPlot(plot).LoadSeries(CreateViewModel(
            [Series(RecordedSessionSignalSeriesRole.FrontSuspension, 10, 20, 15)]));

        Assert.Empty(plot.PlottableList.OfType<HorizontalSpan>());
    }

    [Fact]
    public void LoadSeries_ShowsEmptyMessage_WhenNoRenderableSeries()
    {
        var plot = new Plot();
        // A single-sample series cannot be rendered (needs >= 2 points) and is dropped,
        // leaving the empty state.
        new ExtensionSignalPlot(plot).LoadSeries(CreateViewModel(
            [Series(RecordedSessionSignalSeriesRole.FrontSuspension, 42)]));

        Assert.Empty(plot.PlottableList.OfType<Scatter>());
        Assert.Contains(
            "No matched data",
            plot.PlottableList.OfType<Text>().SelectMany(PlotTestHelpers.ReadTextLabels));
    }

    [Fact]
    public void LoadSeries_SegmentedRuns_DoNotConnectOrExposeCursorValuesAcrossGap()
    {
        var plot = new Plot();
        var sut = new ExtensionSignalPlot(plot)
        {
            MaximumDisplayHz = 30,
            SmoothingLevel = PlotSmoothingLevel.Strong,
        };
        var runs = new[]
        {
            new RecordedSessionSignalRun(
                Enumerable.Range(0, 11).Select(value => value / 100.0).ToArray(),
                Enumerable.Range(0, 11).Select(value => (double)value).ToArray()),
            new RecordedSessionSignalRun(
                Enumerable.Range(0, 11).Select(value => 1.0 + value / 100.0).ToArray(),
                Enumerable.Range(100, 11).Select(value => (double)value).ToArray()),
        };

        sut.LoadSeries(CreateViewModel(
        [
            new RecordedSessionSignalSeries(
                RecordedSessionSignalSeriesRole.FrameImu,
                "Frame",
                "g",
                [],
                [],
                "0.#",
                runs),
        ]));

        Assert.Equal(2, plot.PlottableList.OfType<Scatter>().Count());

        sut.SetCursorPositionWithReadout(1.05);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Frame:", tooltip.LabelText);

        sut.SetCursorPositionWithReadout(0.6);

        Assert.False(tooltip.IsVisible);
    }

    [Fact]
    public void LoadSeries_SegmentedRuns_UseAllRunsForValueLimits()
    {
        var plot = new Plot();
        var sut = new ExtensionSignalPlot(plot);

        sut.LoadSeries(CreateViewModel(
        [
            new RecordedSessionSignalSeries(
                RecordedSessionSignalSeriesRole.FrameImu,
                "Frame",
                "g",
                [],
                [],
                "0.###",
                [
                    new RecordedSessionSignalRun([0, 0.1], [-10, -5]),
                    new RecordedSessionSignalRun([1, 1.1], [100, 110]),
                ]),
        ]));

        var limits = plot.Axes.GetLimits();
        Assert.Equal(-19.6, limits.Bottom, precision: 6);
        Assert.Equal(119.6, limits.Top, precision: 6);
    }

    [Fact]
    public void GetYValues_HandlesAllKnownValueForms()
    {
        Assert.Equal(
            [1, 2],
            ExtensionSignalPlot.GetYValues(TimeSeries(new SampledValues([1, 2], 100))));
        Assert.Equal(
            [3, 4],
            ExtensionSignalPlot.GetYValues(TimeSeries(new ExplicitValues([0, 1], [3, 4]))));
        Assert.Equal(
            [5, 6, 7, 8],
            ExtensionSignalPlot.GetYValues(TimeSeries(new SegmentedValues(
            [
                new ExplicitValues([0, 1], [5, 6]),
                new ExplicitValues([2, 3], [7, 8]),
            ]))));
    }

    [Fact]
    public void GetYValues_ThrowsForUnsupportedValueForm()
    {
        var exception = Assert.Throws<NotSupportedException>(() =>
            ExtensionSignalPlot.GetYValues(TimeSeries(new UnsupportedValues())).ToArray());

        Assert.Contains(nameof(UnsupportedValues), exception.Message);
    }

    [Fact]
    public void LoadSeries_SegmentedRuns_PreserveMeasuredZero()
    {
        var plot = new Plot();
        var sut = new ExtensionSignalPlot(plot);

        sut.LoadSeries(CreateViewModel(
        [
            new RecordedSessionSignalSeries(
                RecordedSessionSignalSeriesRole.FrameImu,
                "Frame",
                "g",
                [],
                [],
                "0.###",
                [new RecordedSessionSignalRun([0, 0.1], [0, 1])]),
        ]));

        sut.SetCursorPositionWithReadout(0);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Frame: 0 g", tooltip.LabelText);
    }

    [Fact]
    public void LoadSeries_HidesLegend_ForSingleSeries()
    {
        var plot = new Plot();
        new ExtensionSignalPlot(plot).LoadSeries(CreateViewModel(
            [Series(RecordedSessionSignalSeriesRole.FrontSuspension, 1, 2, 3)]));

        Assert.False(plot.Legend.IsVisible);
    }

    [Fact]
    public void LoadSeries_ShowsLegend_ForMultipleSeries()
    {
        var plot = new Plot();
        new ExtensionSignalPlot(plot).LoadSeries(CreateViewModel(
        [
            Series(RecordedSessionSignalSeriesRole.FrontSuspension, 1, 2, 3),
            Series(RecordedSessionSignalSeriesRole.RearSuspension, 4, 5, 6),
        ]));

        Assert.True(plot.Legend.IsVisible);
    }

    private static (byte, byte, byte, byte) Argb(Color color) => (color.R, color.G, color.B, color.A);

    private static RecordedTimeSeries TimeSeries(RecordedTimeSeriesValues values) =>
        new("Series", "unit", Colors.Black, values);

    private static RecordedSessionSignalSeries Series(RecordedSessionSignalSeriesRole role, params double[] values)
    {
        var seconds = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            seconds[index] = index;
        }

        return new RecordedSessionSignalSeries(role, role.ToString(), "unit", seconds, values);
    }

    private static RecordedSessionSignalPlotViewModel CreateViewModel(
        IReadOnlyList<RecordedSessionSignalSeries> series,
        bool invertValueAxis = false,
        IReadOnlyList<RecordedSessionSignalSpan>? airtimeSpans = null) =>
        new(series, invertValueAxis, durationSeconds: 100, emptyMessage: "No matched data", airtimeSpans ?? []);

    private sealed record UnsupportedValues : RecordedTimeSeriesValues;
}
