using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Tests.TestSupport;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.TestTelemetryData;

using Sufni.App.LiveDaq.Plots;
namespace Sufni.App.Tests.Plots;

public class ImuPlotTests
{
    [Fact]
    public void LoadTelemetryData_AddsOneVibrationSeriesPerActiveLocationWithMetadata()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);

        sut.LoadTelemetryData(CreateWithImu());

        Assert.NotNull(sut.CursorLine);
        Assert.Empty(plot.Axes.Title.Label.Text);
        Assert.Equal(2, plot.PlottableList.OfType<Signal>().Count());
        Assert.True(plot.Legend.IsVisible);
        Assert.Equal(
            ["Frame", "Fork"],
            plot.PlottableList.OfType<Signal>().Select(signal => signal.LegendText).ToArray());
        Assert.Equal(2, plot.Axes.Rules.Count);
    }

    [Fact]
    public void LoadTelemetryData_SkipsLocationsWithoutMetadata()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);

        var telemetry = CreateWithImu(
            activeLocations: [0, 1],
            meta: [new ImuMetaEntry(0, 1.0f, 1.0f)],
            records:
            [
                new ImuRecord(1, 0, 1, 0, 0, 0),
                new ImuRecord(2, 0, 1, 0, 0, 0),
                new ImuRecord(3, 0, 1, 0, 0, 0),
                new ImuRecord(4, 0, 1, 0, 0, 0)
            ]);

        sut.LoadTelemetryData(telemetry);

        Assert.Single(plot.PlottableList.OfType<Signal>());
    }

    [Fact]
    public void LoadTelemetryData_WithGappedImuData_RendersSegmentedScatterSeries()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);
        var telemetry = CreateMinimal();
        telemetry.ImuData = new RawImuData
        {
            SampleRate = 10,
            ActiveLocations = [(byte)ImuLocation.Frame],
            Meta = [new ImuMetaEntry((byte)ImuLocation.Frame, 1000, 100)],
            HasGaps = true,
            Segments =
            [
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstMonotonicDeltaUs = 0,
                    Records =
                    [
                        new ImuRecord(0, 0, 1000, 0, 0, 0),
                        new ImuRecord(0, 0, 1000, 0, 0, 0),
                    ],
                },
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstMonotonicDeltaUs = 1_000_000,
                    Records =
                    [
                        new ImuRecord(0, 0, 1000, 0, 0, 0),
                        new ImuRecord(0, 0, 1000, 0, 0, 0),
                    ],
                },
            ],
        };

        sut.LoadTelemetryData(telemetry);

        var scatters = plot.PlottableList.OfType<Scatter>().ToArray();
        Assert.Equal(2, scatters.Length);
        Assert.Empty(plot.PlottableList.OfType<Signal>());
        Assert.Equal("Frame", scatters[0].LegendText);
        Assert.True(string.IsNullOrEmpty(scatters[1].LegendText));
    }

    [Fact]
    public void LoadTelemetryData_ShowsEmptyState_WhenImuDataIsMissing()
    {
        var plot = new Plot();
        var sut = new ImuPlot(plot);

        sut.LoadTelemetryData(CreateMinimal());

        Assert.Null(sut.CursorLine);
        Assert.Empty(plot.Axes.Title.Label.Text);
        Assert.Empty(plot.PlottableList.OfType<Signal>());
        Assert.Equal(2, plot.Axes.Rules.Count);
        Assert.Single(plot.PlottableList.OfType<Text>());
    }
}
