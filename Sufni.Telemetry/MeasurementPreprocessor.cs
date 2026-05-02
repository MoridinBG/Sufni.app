namespace Sufni.Telemetry;

public enum MeasurementSensorType
{
    Linear,
    Rotational,
}

public readonly record struct MeasurementPreprocessorResult(
    ushort[] Samples,
    int AnomalyCount);

public static class MeasurementPreprocessor
{
    private const int AdcCircularRange = 4096;
    private const int AdcCircularHalfRange = AdcCircularRange / 2;

    public static MeasurementPreprocessorResult Process(
        ushort[] samples,
        MeasurementSensorType sensorType)
    {
        var signal = sensorType switch
        {
            MeasurementSensorType.Linear => Array.ConvertAll(samples, value => (int)value),
            MeasurementSensorType.Rotational => UnwrapCircularSamples(samples),
            _ => throw new ArgumentOutOfRangeException(nameof(sensorType), sensorType, null),
        };

        var fixedSignal = SpikeElimination.EliminateSpikesAsInt(signal);
        var fixedSamples = sensorType switch
        {
            MeasurementSensorType.Linear => Array.ConvertAll(fixedSignal.fixedSignal, ClampLinearSample),
            MeasurementSensorType.Rotational => Array.ConvertAll(fixedSignal.fixedSignal, WrapCircularSample),
            _ => throw new ArgumentOutOfRangeException(nameof(sensorType), sensorType, null),
        };

        return new MeasurementPreprocessorResult(fixedSamples, fixedSignal.anomalyCount);
    }

    public static MeasurementSensorType SensorTypeForWrapping(bool measurementWraps)
    {
        return measurementWraps
            ? MeasurementSensorType.Rotational
            : MeasurementSensorType.Linear;
    }

    private static int[] UnwrapCircularSamples(IReadOnlyList<ushort> samples)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var unwrapped = new int[samples.Count];
        var offset = 0;
        var previous = NormalizeCircularSample(samples[0]);
        unwrapped[0] = previous;

        for (var index = 1; index < samples.Count; index++)
        {
            var current = NormalizeCircularSample(samples[index]);
            var delta = current - previous;
            if (delta > AdcCircularHalfRange)
            {
                offset -= AdcCircularRange;
            }
            else if (delta < -AdcCircularHalfRange)
            {
                offset += AdcCircularRange;
            }

            unwrapped[index] = current + offset;
            previous = current;
        }

        return unwrapped;
    }

    private static int NormalizeCircularSample(ushort sample) => sample % AdcCircularRange;

    private static ushort WrapCircularSample(int sample)
    {
        var wrapped = sample % AdcCircularRange;
        if (wrapped < 0)
        {
            wrapped += AdcCircularRange;
        }

        return (ushort)wrapped;
    }

    private static ushort ClampLinearSample(int sample) => (ushort)Math.Clamp(sample, 0, AdcCircularRange - 1);
}