using Sufni.App.Plots;

namespace Sufni.App.Tests.Plots;

public class TelemetryDisplayDownsamplingTests
{
    [Fact]
    public void Prepare_ReturnsOriginalSamples_WhenSampleRateIsAtOrBelowCap()
    {
        var samples = new[] { 0.0, 1.0, 2.0, 3.0 };

        var (prepared, step) = TelemetryDisplayDownsampling.Prepare(samples, sampleRate: 100, maximumDisplayHz: 100);

        Assert.Same(samples, prepared);
        Assert.Equal(0.01, step, 6);
    }

    [Fact]
    public void Prepare_Downsamples_AndAdjustsStep_WhenSampleRateExceedsCap()
    {
        var samples = Enumerable.Range(0, 25).Select(static value => (double)value).ToArray();

        var (prepared, step) = TelemetryDisplayDownsampling.Prepare(samples, sampleRate: 1000, maximumDisplayHz: 100);

        Assert.Equal([0.0, 10.0, 20.0], prepared);
        Assert.Equal(0.01, step, 6);
    }

    [Fact]
    public void Prepare_UsesCeilingStride_ForNonIntegralRatios()
    {
        var samples = Enumerable.Range(0, 10).Select(static value => (double)value).ToArray();

        var (prepared, step) = TelemetryDisplayDownsampling.Prepare(samples, sampleRate: 250, maximumDisplayHz: 100);

        Assert.Equal([0.0, 3.0, 6.0, 9.0], prepared);
        Assert.Equal(0.012, step, 6);
    }
}