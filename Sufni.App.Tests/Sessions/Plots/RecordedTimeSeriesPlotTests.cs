using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

using Sufni.App.Sessions.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Shared.Plots;
namespace Sufni.App.Tests.Sessions.Plots;

public class RecordedTimeSeriesPlotTests
{
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

    private sealed class TestRecordedTimeSeriesPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
    {
        public void LoadForTest(RecordedTimeSeriesData data)
        {
            LoadTimeSeries(data);
        }
    }
}
