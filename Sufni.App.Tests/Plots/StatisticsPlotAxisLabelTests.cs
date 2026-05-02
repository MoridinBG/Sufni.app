using ScottPlot;
using Sufni.App.Plots;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Plots;

public class StatisticsPlotAxisLabelTests
{
    [Fact]
    public void TravelHistogram_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new TravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Time (%)", "Axle position (mm)");
    }

    [Fact]
    public void VelocityHistogram_WithStrokePeakMode_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front)
        {
            AverageMode = VelocityAverageMode.StrokePeakAveraged,
        };

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Strokes (%)", "Velocity (mm/s)");
    }

    [Fact]
    public void VelocityHistogram_WithSampleMode_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new VelocityHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Time (%)", "Velocity (mm/s)");
    }

    [Fact]
    public void DeepTravelHistogram_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new DeepTravelHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Axle position (mm)", "Strokes");
    }

    [Fact]
    public void StrokeLengthHistogram_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new StrokeLengthHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Stroke length (mm)", "Strokes (%)");
    }

    [Fact]
    public void StrokeSpeedHistogram_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new StrokeSpeedHistogramPlot(plot, SuspensionType.Front, BalanceType.Compression);

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Peak stroke speed (mm/s)", "Strokes (%)");
    }

    [Fact]
    public void Balance_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new BalancePlot(plot, BalanceType.Compression);

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Zenith (%)", "Peak speed (mm/s)");
    }

    [Fact]
    public void TravelFrequencyHistogram_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new TravelFrequencyHistogramPlot(plot, SuspensionType.Front);

        sut.LoadTelemetryData(TestTelemetryData.Create(frontPresent: true, rearPresent: true));

        AssertAxisLabels(plot, "Frequency (Hz)", "Power (dB)");
    }

    [Fact]
    public void VibrationThirds_RendersAxisLabels()
    {
        var plot = new Plot();
        var sut = new VibrationThirdsPlot(plot, SuspensionType.Front, ImuLocation.Fork);

        sut.LoadTelemetryData(CreateTelemetryWithForkImu());

        AssertAxisLabels(plot, "Stroke group", "Vibration (%)");
    }

    private static void AssertAxisLabels(Plot plot, string expectedBottom, string expectedLeft)
    {
        Assert.Equal(expectedBottom, plot.Axes.Bottom.Label.Text);
        Assert.Equal(expectedLeft, plot.Axes.Left.Label.Text);
    }

    private static TelemetryData CreateTelemetryWithForkImu()
    {
        var telemetry = TestTelemetryData.Create(frontPresent: true, rearPresent: true);
        const float accelLsbPerG = 1000;

        telemetry.ImuData = new RawImuData
        {
            SampleRate = telemetry.Metadata.SampleRate,
            ActiveLocations = [(byte)ImuLocation.Fork],
            Meta = [new ImuMetaEntry((byte)ImuLocation.Fork, accelLsbPerG, 1)],
            Records = Enumerable.Range(0, telemetry.Front.Travel.Length)
                .Select(index => new ImuRecord(0, 0, (short)(accelLsbPerG + 100 + index % 10), 0, 0, 0))
                .ToList(),
        };

        return telemetry;
    }
}
