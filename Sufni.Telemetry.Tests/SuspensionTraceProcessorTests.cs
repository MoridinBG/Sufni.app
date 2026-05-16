using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SuspensionTraceProcessorTests
{
    [Fact]
    public void Process_ConvertsMeasurementsToClampedTravel()
    {
        ushort[] measurements = [0, 1, 10, 20, 30];
        double[] time = [0, 0.001, 0.002, 0.003, 0.004];
        var filter = SavitzkyGolay.Create(5, 1, 3);

        var result = SuspensionTraceProcessor.Process(
            measurements,
            maxTravel: 2,
            measurementToTravel: measurement => measurement,
            sampleRate: 1000,
            time,
            filter);

        Assert.Equal([0, 1, 2, 2, 2], result.Travel);
        Assert.False(result.Present);
    }

    [Fact]
    public void Process_WithCompressionAndRebound_GeneratesVelocityBinsAndStrokes()
    {
        var measurements = new ushort[200];
        for (var index = 0; index < 100; index++)
        {
            measurements[index] = (ushort)(index * 2);
        }
        for (var index = 100; index < 200; index++)
        {
            measurements[index] = (ushort)(200 - (index - 100) * 2);
        }

        var time = Enumerable.Range(0, measurements.Length)
            .Select(index => index / 1000.0)
            .ToArray();
        var filter = SavitzkyGolay.Create(51, 1, 3);

        var result = SuspensionTraceProcessor.Process(
            measurements,
            maxTravel: 20,
            measurementToTravel: measurement => measurement / 10.0,
            sampleRate: 1000,
            time,
            filter);

        Assert.True(result.Present);
        Assert.Equal(measurements.Length, result.Travel.Length);
        Assert.Equal(measurements.Length, result.Velocity.Length);
        Assert.NotEmpty(result.TravelBins);
        Assert.NotEmpty(result.VelocityBins);
        Assert.NotEmpty(result.FineVelocityBins);
        Assert.NotEmpty(result.Strokes.Compressions);
        Assert.NotEmpty(result.Strokes.Rebounds);
    }

    [Fact]
    public void Process_WithNullVelocityFilter_UsesUnfilteredVelocity()
    {
        ushort[] measurements = [0, 10, 30, 60, 100];
        double[] time = [0, 0.001, 0.002, 0.003, 0.004];

        var result = SuspensionTraceProcessor.Process(
            measurements,
            maxTravel: 200,
            measurementToTravel: measurement => measurement,
            sampleRate: 1000,
            time,
            velocityFilter: null);

        Assert.Equal([10000, 15000, 25000, 35000, 40000], result.Velocity);
    }
}
