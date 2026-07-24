using System.Numerics;
using System.Numerics.Tensors;
using MathNet.Numerics.IntegralTransforms;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    private const int MinimumFrequencyResolutionDurationMilliseconds = 20_000;

    public static HistogramData CalculateTravelFrequencyHistogram(
        TelemetryData telemetryData,
        SuspensionType type,
        TelemetryTimeRange? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var travelSamples = GetTravelSamples(telemetryData, suspension, range);
        if (travelSamples.Length < 2 || telemetryData.Metadata.SampleRate <= 0)
        {
            return new HistogramData([], []);
        }

        var mean = TensorPrimitives.Sum<double>(travelSamples) / travelSamples.Length;

        var minimumCount = Math.Max(
            2,
            (int)Math.Ceiling(MinimumFrequencyResolutionDurationMilliseconds / 1000.0 * telemetryData.Metadata.SampleRate));
        var count = Math.Max(minimumCount, travelSamples.Length);
        var complexSignal = new Complex[count];

        for (var index = 0; index < travelSamples.Length; index++)
        {
            complexSignal[index] = new Complex(travelSamples[index] - mean, 0);
        }

        for (var index = travelSamples.Length; index < count; index++)
        {
            complexSignal[index] = Complex.Zero;
        }

        Fourier.Forward(complexSignal, FourierOptions.Matlab);

        var halfCount = count / 2 + 1;
        var tick = 1.0 / telemetryData.Metadata.SampleRate;
        var retainedCapacity = (int)Math.Min(
            halfCount,
            Math.Floor(10 * count * tick) + 1);
        var frequencies = new List<double>(retainedCapacity);
        var spectrum = new List<double>(retainedCapacity);

        for (var index = 0; index < halfCount; index++)
        {
            var frequency = index / (count * tick);
            if (frequency > 10)
            {
                break;
            }

            frequencies.Add(frequency);
            var value = complexSignal[index];
            spectrum.Add(value.Magnitude * value.Magnitude);
        }

        return new HistogramData(frequencies, spectrum);
    }
}
