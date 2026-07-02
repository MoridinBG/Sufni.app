using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Theming;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Extensibility.Views;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Plots;
using Sufni.App.Shared.Plots;
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
}
