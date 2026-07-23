using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetryData;

using Sufni.App.LiveDaq.Plots;
using Sufni.App.LiveDaq.Services.Imu;
namespace Sufni.App.Tests.LiveDaq.Plots;

public class FramePitchRollPlotTests
{
    [Fact]
    public void LoadProjection_AddsPitchAndRollSeries_WhenFrameImuHasGyroMetadata()
    {
        var plot = new Plot();
        var sut = new FramePitchRollPlot(plot);
        var telemetry = CreateTelemetryDataWithFramePitchRoll();

        sut.LoadProjection(telemetry, ImuDisplaySignalProcessor.ProcessRecorded(telemetry));

        Assert.NotNull(sut.CursorLine);
        Assert.Empty(plot.Axes.Title.Label.Text);
        Assert.Equal(
            ["Pitch", "Roll"],
            plot.PlottableList.OfType<Signal>().Select(signal => signal.LegendText).ToArray());
        Assert.True(plot.Legend.IsVisible);
        Assert.True(plot.Axes.Left.Min < 0);
        Assert.True(plot.Axes.Left.Max > 0);
        Assert.Equal(Math.Abs(plot.Axes.Left.Min), plot.Axes.Left.Max, precision: 6);
    }

    [Fact]
    public void LoadProjection_WithGappedFrameImu_RendersPitchAndRollAsSegmentedScatters()
    {
        var plot = new Plot();
        var sut = new FramePitchRollPlot(plot);
        var telemetry = CreateMinimal();
        telemetry.ImuData = new RawImuData
        {
            SampleRate = 10,
            ActiveLocations = [(byte)ImuLocation.Frame],
            Meta = [new ImuMetaEntry((byte)ImuLocation.Frame, 10, 100)],
            HasGaps = true,
            Segments =
            [
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstMonotonicDeltaUs = 0,
                    Records = [Rest(), Rest()],
                },
                new RawImuSegment
                {
                    LocationId = (byte)ImuLocation.Frame,
                    FirstMonotonicDeltaUs = 1_000_000,
                    Records = [Rest(), new ImuRecord(2, 0, 10, 0, 30, 0)],
                },
            ],
        };

        sut.LoadProjection(telemetry, ImuDisplaySignalProcessor.ProcessRecorded(telemetry));

        var scatters = plot.PlottableList.OfType<Scatter>().ToArray();
        Assert.Equal(4, scatters.Length);
        Assert.Empty(plot.PlottableList.OfType<Signal>());
        Assert.Equal(["Pitch", "", "Roll", ""], scatters.Select(scatter => scatter.LegendText ?? string.Empty).ToArray());
    }

    [Fact]
    public void LoadProjection_ShowsEmptyState_WhenFramePitchRollIsUnavailable()
    {
        var plot = new Plot();
        var sut = new FramePitchRollPlot(plot);
        var telemetry = CreateWithImu(
            activeLocations: [(byte)ImuLocation.Fork],
            meta: [new ImuMetaEntry((byte)ImuLocation.Fork, 10, 100)],
            records:
            [
                Rest(),
                Rest(),
                Rest(),
                Rest(),
                Rest(),
                new ImuRecord(2, 0, 10, 0, 30, 0),
            ],
            sampleRate: 10);

        sut.LoadProjection(telemetry, ImuDisplaySignalProcessor.ProcessRecorded(telemetry));

        Assert.Null(sut.CursorLine);
        Assert.Empty(plot.Axes.Title.Label.Text);
        Assert.Empty(plot.PlottableList.OfType<Signal>());
        Assert.Single(plot.PlottableList.OfType<Text>());
    }

    private static TelemetryData CreateTelemetryDataWithFramePitchRoll()
    {
        return CreateWithImu(
            activeLocations: [(byte)ImuLocation.Frame],
            meta: [new ImuMetaEntry((byte)ImuLocation.Frame, 10, 100)],
            records:
            [
                Rest(),
                Rest(),
                Rest(),
                Rest(),
                Rest(),
                new ImuRecord(2, 0, 10, 0, 30, 0),
            ],
            sampleRate: 10);
    }

    private static ImuRecord Rest() => new(0, 0, 10, 0, 0, 0);
}
