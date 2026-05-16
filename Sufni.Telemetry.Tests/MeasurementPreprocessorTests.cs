using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class MeasurementPreprocessorTests
{
    [Fact]
    public void Process_WithLinearHighAdcSamples_DoesNotWrapMeasurements()
    {
        ushort[] samples = [2500, 2501, 2502, 2503];

        var result = MeasurementPreprocessor.Process(samples, MeasurementSensorType.Linear, sampleRate: 1000);

        Assert.Equal(samples, result.Samples);
        Assert.Equal(0, result.AnomalyCount);
    }

    [Fact]
    public void Process_WithLinearOutOfRangeSamples_ClampsToAdcRange()
    {
        ushort[] samples = [4096, 4096, 4096];

        var result = MeasurementPreprocessor.Process(samples, MeasurementSensorType.Linear, sampleRate: 1000);

        Assert.Equal([4095, 4095, 4095], result.Samples);
    }

    [Fact]
    public void Process_WithLowerSampleRateRampBelowStepRate_DoesNotFlattenMeasurements()
    {
        ushort[] samples = [0, 40, 80, 120, 160, 160, 160];

        var result = MeasurementPreprocessor.Process(samples, MeasurementSensorType.Linear, sampleRate: 200);

        Assert.Equal(samples, result.Samples);
        Assert.Equal(0, result.AnomalyCount);
    }

    [Fact]
    public void Process_WithCircularWrapEdgeSamples_DoesNotCreateSpikeFault()
    {
        var samples = new ushort[240];
        Array.Fill(samples, (ushort)60);
        for (var index = 120; index < 125; index++)
        {
            samples[index] = 4095;
        }

        var result = MeasurementPreprocessor.Process(samples, MeasurementSensorType.Rotational, sampleRate: 1000);

        Assert.Equal(0, result.AnomalyCount);
        Assert.Equal((ushort)60, result.Samples[130]);
        Assert.Equal((ushort)60, result.Samples[239]);
    }

    [Fact]
    public void Process_WithCircularSamplesAcrossZero_PreservesWrappedMeasurements()
    {
        ushort[] samples = [4094, 4095, 0, 1, 2];

        var result = MeasurementPreprocessor.Process(samples, MeasurementSensorType.Rotational, sampleRate: 1000);

        Assert.Equal(samples, result.Samples);
        Assert.Equal(0, result.AnomalyCount);
    }
}
