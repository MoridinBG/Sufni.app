using System.Buffers;

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
        MeasurementSensorType sensorType,
        int sampleRate)
    {
        var signal = ArrayPool<int>.Shared.Rent(samples.Length);
        try
        {
            var signalSpan = signal.AsSpan(0, samples.Length);
            switch (sensorType)
            {
                case MeasurementSensorType.Linear:
                    for (var index = 0; index < samples.Length; index++)
                    {
                        signalSpan[index] = samples[index];
                    }

                    break;
                case MeasurementSensorType.Rotational:
                    UnwrapCircularSamples(samples, signalSpan);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sensorType), sensorType, null);
            }

            var anomalyCount = SpikeElimination.EliminateSpikesAsInt(signalSpan, sampleRate);
            var fixedSamples = new ushort[samples.Length];
            switch (sensorType)
            {
                case MeasurementSensorType.Linear:
                    for (var index = 0; index < samples.Length; index++)
                    {
                        fixedSamples[index] = ClampLinearSample(signalSpan[index]);
                    }

                    break;
                case MeasurementSensorType.Rotational:
                    for (var index = 0; index < samples.Length; index++)
                    {
                        fixedSamples[index] = WrapCircularSample(signalSpan[index]);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sensorType), sensorType, null);
            }

            return new MeasurementPreprocessorResult(fixedSamples, anomalyCount);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(signal);
        }
    }

    public static MeasurementSensorType SensorTypeForWrapping(bool measurementWraps)
    {
        return measurementWraps
            ? MeasurementSensorType.Rotational
            : MeasurementSensorType.Linear;
    }

    private static void UnwrapCircularSamples(ushort[] samples, Span<int> unwrapped)
    {
        if (samples.Length == 0)
        {
            return;
        }

        var offset = 0;
        var previous = NormalizeCircularSample(samples[0]);
        unwrapped[0] = previous;

        for (var index = 1; index < samples.Length; index++)
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
