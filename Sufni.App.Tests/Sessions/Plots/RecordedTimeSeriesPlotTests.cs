using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Theming;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Plots;

using Sufni.App.Sessions.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Shared.Plots;
namespace Sufni.App.Tests.Sessions.Plots;

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
                    new ExplicitValues([0, 0.1, 0.2], [0, 9, 0]),
                    "0.#")
            ]));

        var scatter = Assert.Single(plot.PlottableList.OfType<Scatter>());
        Assert.False(scatter.MarkerStyle.IsVisible);
        Assert.Empty(plot.PlottableList.OfType<Signal>());
        Assert.NotNull(sut.CursorLine);

        sut.SetCursorPositionWithReadout(0.1);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 6.9 km/h", tooltip.LabelText);
    }

    [Fact]
    public void LoadTimeSeries_DownsamplesExplicitValuesForDisplayAndCursor()
    {
        var plot = new Plot();
        var sut = new TestRecordedTimeSeriesPlot(plot)
        {
            MaximumDisplayHz = 30,
        };

        sut.LoadForTest(new RecordedTimeSeriesData(
            "Speed (km/h)",
            "No speed data",
            DurationSeconds: 0.1,
            Series:
            [
                new RecordedTimeSeries(
                    "Speed",
                    "km/h",
                    Color.FromHex("#ffffbf"),
                    new ExplicitValues(
                        Enumerable.Range(0, 11).Select(static value => value / 100.0).ToArray(),
                        Enumerable.Range(0, 11).Select(static value => (double)value).ToArray()),
                    "0.#")
            ]));

        sut.SetCursorPositionWithReadout(0.05);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 4 km/h", tooltip.LabelText);

        sut.SetCursorPositionWithReadout(0.1);

        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 10 km/h", tooltip.LabelText);
    }

    [Fact]
    public void LoadTimeSeries_RendersSegmentedValuesAsScattersAndCursorIgnoresGaps()
    {
        var plot = new Plot();
        var sut = new TestRecordedTimeSeriesPlot(plot);

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
                    new SegmentedValues(
                    [
                        new ExplicitValues([0, 0.1, 0.2], [0, 10, 20]),
                        new ExplicitValues([1.0, 1.1, 1.2], [30, 40, 50])
                    ]),
                    "0.#")
            ]));

        var scatters = plot.PlottableList.OfType<Scatter>().ToArray();
        Assert.Equal(2, scatters.Length);
        Assert.Equal("Speed", scatters[0].LegendText);
        Assert.True(string.IsNullOrEmpty(scatters[1].LegendText));
        Assert.Empty(plot.PlottableList.OfType<Signal>());

        sut.SetCursorPositionWithReadout(0.1);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 10 km/h", tooltip.LabelText);

        sut.SetCursorPositionWithReadout(0.6);

        Assert.False(tooltip.IsVisible);
    }

    [Fact]
    public void LoadTimeSeries_DownsamplesSegmentedValuesAndCursorIgnoresDecimatedGaps()
    {
        var plot = new Plot();
        var sut = new TestRecordedTimeSeriesPlot(plot)
        {
            MaximumDisplayHz = 30,
        };

        sut.LoadForTest(new RecordedTimeSeriesData(
            "Speed (km/h)",
            "No speed data",
            DurationSeconds: 1.1,
            Series:
            [
                new RecordedTimeSeries(
                    "Speed",
                    "km/h",
                    Color.FromHex("#ffffbf"),
                    new SegmentedValues(
                    [
                        new ExplicitValues(
                            Enumerable.Range(0, 11).Select(static value => value / 100.0).ToArray(),
                            Enumerable.Range(0, 11).Select(static value => (double)value).ToArray()),
                        new ExplicitValues(
                            Enumerable.Range(0, 11).Select(static value => 1.0 + value / 100.0).ToArray(),
                            Enumerable.Range(100, 11).Select(static value => (double)value).ToArray())
                    ]),
                    "0.#")
            ]));

        sut.SetCursorPositionWithReadout(0.05);

        var tooltip = Assert.Single(plot.PlottableList.OfType<Tooltip>());
        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 4 km/h", tooltip.LabelText);

        sut.SetCursorPositionWithReadout(0.6);

        Assert.False(tooltip.IsVisible);

        sut.SetCursorPositionWithReadout(1.05);

        Assert.True(tooltip.IsVisible);
        Assert.Contains("Speed: 104 km/h", tooltip.LabelText);
    }

    [Fact]
    public void RangeOverlays_RenderSelectedAndPreviewRanges()
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

        var analysisRegistration = RecordedTimeRangeOverlayFactory.CreateAnalysisRangeRegistration(
            new TelemetryTimeRange(0.25, 0.75),
            SufniThemes.Dark.Plot);
        sut.SetRangeOverlaySet(analysisRegistration.Id, analysisRegistration.Set);
        sut.SetRangeOverlayVisibility(analysisRegistration.Id, analysisRegistration.IsVisible);

        var previewRegistration = RecordedTimeRangeOverlayFactory.CreatePreviewRangeRegistration(
            0.5,
            1.0,
            SufniThemes.Dark.Plot);
        sut.SetRangeOverlaySet(previewRegistration.Id, previewRegistration.Set);
        sut.SetRangeOverlayVisibility(previewRegistration.Id, previewRegistration.IsVisible);

        var spans = plot.PlottableList.OfType<HorizontalSpan>().ToArray();
        Assert.Single(spans, span => span.X1 == 0.25 && span.X2 == 0.75);
        Assert.Single(spans, span => span.X1 == 0.5 && span.X2 == 1.0);
    }

    [Fact]
    public void SetRangeOverlayVisibility_UnknownIdDoesNotPreCreateVisibleState()
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

        sut.SetRangeOverlayVisibility(RecordedTimeRangeOverlayIds.PreviewRange, true);
        var registration = RecordedTimeRangeOverlayFactory.CreatePreviewRangeRegistration(
            0.5,
            1.0,
            SufniThemes.Dark.Plot);
        sut.SetRangeOverlaySet(registration.Id, registration.Set);

        var span = Assert.Single(plot.PlottableList.OfType<HorizontalSpan>());
        Assert.False(span.IsVisible);
    }

    [Fact]
    public void AnalysisSelectionRangeOverlay_UsesSuspensionSpecificStylesAndVisibility()
    {
        var plot = new Plot();
        var sut = new TestRecordedTimeSeriesPlot(plot);
        sut.LoadForTest(new RecordedTimeSeriesData(
            "Travel (mm)",
            "No travel data",
            DurationSeconds: 2,
            Series:
            [
                new RecordedTimeSeries(
                    "Front",
                    "mm",
                    TelemetryPlot.FrontColor,
                    new SampledValues([0, 25, 50, 75], SampleRate: 2),
                    "0.#")
            ]));

        var theme = SufniThemes.Dark.Plot;
        var registration = RecordedTimeRangeOverlayFactory.CreateAnalysisSelectionRegistration(
            [
                new TelemetryHighlightRange(0.25, 0.5, SuspensionType.Front),
                new TelemetryHighlightRange(0.75, 1.0, SuspensionType.Rear),
            ],
            theme);

        sut.SetRangeOverlaySet(registration.Id, registration.Set);
        sut.SetRangeOverlayVisibility(registration.Id, registration.IsVisible);

        var spans = plot.PlottableList.OfType<HorizontalSpan>().ToArray();
        Assert.Equal(2, spans.Length);
        Assert.All(spans, span => Assert.False(span.IsVisible));
        Assert.Equal(theme.Marker.AnalysisSelectionFrontFill.ToScottPlotColor(), spans[0].FillColor);
        Assert.Equal(theme.Marker.AnalysisSelectionFrontOutline.ToScottPlotColor(), spans[0].LineStyle.Color);
        Assert.Equal(theme.Marker.AnalysisSelectionRearFill.ToScottPlotColor(), spans[1].FillColor);
        Assert.Equal(theme.Marker.AnalysisSelectionRearOutline.ToScottPlotColor(), spans[1].LineStyle.Color);

        sut.SetRangeOverlayVisibility(RecordedTimeRangeOverlayIds.AnalysisSelection, true);

        Assert.All(spans, span => Assert.True(span.IsVisible));
    }

    private sealed class TestRecordedTimeSeriesPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
    {
        public void LoadForTest(RecordedTimeSeriesData data)
        {
            LoadTimeSeries(data);
        }
    }
}
