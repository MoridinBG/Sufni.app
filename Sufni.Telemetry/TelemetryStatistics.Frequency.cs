using System.Collections.Immutable;
using System.Numerics;
using System.Numerics.Tensors;
using MathNet.Numerics.IntegralTransforms;

namespace Sufni.Telemetry;

public closed record TravelFrequencyResult
{
    public sealed record Computed(ImmutableArray<double> Frequencies, ImmutableArray<double> Power) : TravelFrequencyResult;
    public sealed record NoData : TravelFrequencyResult;
    public sealed record InsufficientContinuousData : TravelFrequencyResult;
}

public static partial class TelemetryStatistics
{
    private const int MinimumFrequencyResolutionSeconds = 20;
    private const int MinimumSpectrumIntervalSeconds = 2;

    public static TravelFrequencyResult CalculateTravelFrequencyHistogram(
        TelemetryData telemetryData, SuspensionType type, TelemetryTimeSelection? range = null)
    {
        var suspension = GetSuspension(telemetryData, type);
        var rate = telemetryData.Metadata.SampleRate;
        if (rate <= 0 || suspension.Travel.IsDefaultOrEmpty) return new TravelFrequencyResult.NoData();
        var intervals = SuspensionSampleInterval.Select(suspension.Segments, suspension.Travel.Length, rate, range);
        if (intervals.Count == 0) return new TravelFrequencyResult.NoData();
        intervals.RemoveAll(interval => interval.Count < (long)MinimumSpectrumIntervalSeconds * rate);
        if (intervals.Count == 0) return new TravelFrequencyResult.InsufficientContinuousData();

        var count = Math.Max(checked(MinimumFrequencyResolutionSeconds * rate), intervals.Max(i => i.Count));
        var bins = Math.Min(count / 2 + 1, (int)(10L * count / rate + 1));
        var power = new double[bins];
        var signal = new Complex[count];
        foreach (var interval in intervals)
        {
            var samples = suspension.Travel.AsSpan(interval.Start, interval.Count);
            var mean = TensorPrimitives.Sum<double>(samples) / samples.Length;
            for (var i = 0; i < samples.Length; i++) signal[i] = new Complex(samples[i] - mean, 0);
            Array.Clear(signal, samples.Length, count - samples.Length);
            Fourier.Forward(signal, FourierOptions.Matlab);
            for (var i = 0; i < bins; i++)
                power[i] += signal[i].Magnitude * signal[i].Magnitude;
        }

        return new TravelFrequencyResult.Computed(
            [.. Enumerable.Range(0, bins).Select(i => (double)i * rate / count)], [.. power]);
    }
}
