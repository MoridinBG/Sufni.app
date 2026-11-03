using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SuspensionTraceProcessorTests
{
    [Fact]
    public void Process_ConvertsMeasurementsToClampedTravel()
    {
        ushort[] measurements = [0, 1, 10, 20, 30];
        var filter = SavitzkyGolay.Create(5, 1, 3);

        var result = SuspensionTraceProcessor.Process(
            measurements,
            maxTravel: 2,
            measurementToTravel: measurement => measurement,
            sampleRate: 1000,
            filter);

        Assert.Equal([0, 1, 2, 2, 2], result.Travel);
    }

    [Fact]
    public void Process_WithoutSamples_ThrowsTypedFailure()
    {
        var failure = Assert.Throws<TelemetryProcessingException>(() =>
            SuspensionTraceProcessor.Process([], 100, value => value, 100, velocityFilter: null));

        Assert.Equal(TelemetryProcessingFailureReason.VelocitySamplesMissing, failure.Reason);
    }

    [Fact]
    public void Process_WithNullVelocityFilter_UsesUnfilteredVelocity()
    {
        ushort[] measurements = [0, 10, 30, 60, 100];

        var result = SuspensionTraceProcessor.Process(
            measurements,
            maxTravel: 200,
            measurementToTravel: measurement => measurement,
            sampleRate: 1000,
            velocityFilter: null);

        Assert.Equal([10000, 15000, 25000, 35000, 40000], result.Velocity);
    }
}
