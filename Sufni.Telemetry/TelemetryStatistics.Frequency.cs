using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
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

        var sum = 0.0;
        foreach (var travel in travelSamples)
        {
            sum += travel;
        }
        var mean = sum / travelSamples.Length;

        var count = Math.Max(20000, travelSamples.Length);
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
        var frequencies = new List<double>(halfCount);
        var spectrum = new List<double>(halfCount);

        var tick = 1.0 / telemetryData.Metadata.SampleRate;

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
