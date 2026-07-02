
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class TelemetryProcessingOptionsTests
{
    [Theory]
    [InlineData(1000, 25, 25)]     // odd sample count is kept as-is
    [InlineData(1000, 100, 101)]   // even sample count is forced odd
    [InlineData(100, 10, 5)]       // tiny window is floored at the minimum
    [InlineData(1000, 2000, 1001)] // window clamps to the max milliseconds first
    public void VelocityFilterWindowSamples_ConvertsWindowToOddSampleCount(
        int sampleRate,
        int windowMilliseconds,
        int expectedSamples)
    {
        var options = new TelemetryProcessingOptions(windowMilliseconds);

        Assert.Equal(expectedSamples, options.VelocityFilterWindowSamples(sampleRate));
    }

    [Fact]
    public void VelocityFilterWindowSamples_ReturnsZero_WhenFilterIsOff()
    {
        var options = new TelemetryProcessingOptions(0);

        Assert.Equal(0, options.VelocityFilterWindowSamples(1000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void VelocityFilterWindowSamples_ReturnsZero_WhenSampleRateUnknown(int sampleRate)
    {
        var options = new TelemetryProcessingOptions(
            TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds);

        Assert.Equal(0, options.VelocityFilterWindowSamples(sampleRate));
    }
}
