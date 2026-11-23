using System.Numerics;

namespace Sufni.Telemetry;

/// <summary>Zero-phase Butterworth filters for a single continuous, uniformly sampled segment.</summary>
public static class SignalFilters
{
    public static double[] LowPass(ReadOnlySpan<double> samples, double sampleRate, double cutoff)
    {
        ValidateFrequency(cutoff, sampleRate);
        var k = Math.Tan(Math.PI * cutoff / sampleRate);
        var gain = 1 / (1 + Math.Sqrt(2) * k + k * k);
        var b = k * k * gain;
        var section = new Section(b, 2 * b, b, 2 * (k * k - 1) * gain, (1 - Math.Sqrt(2) * k + k * k) * gain);
        return ForwardBackward(samples, sampleRate, cutoff, [section]);
    }

    public static double[] BandPass(ReadOnlySpan<double> samples, double sampleRate, double lowCutoff, double highCutoff)
    {
        ValidateFrequency(lowCutoff, sampleRate);
        ValidateFrequency(highCutoff, sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(highCutoff, lowCutoff);
        var low = Math.Tan(Math.PI * lowCutoff / sampleRate);
        var high = Math.Tan(Math.PI * highCutoff / sampleRate);
        var centre = Math.Sqrt(low * high);
        var prototypePole = new Complex(-1, 1) / Math.Sqrt(2);
        var halfWidthPole = prototypePole * ((high - low) / 2);
        var root = Complex.Sqrt(halfWidthPole * halfWidthPole - centre * centre);
        var centreDelay = Complex.FromPolarCoordinates(1, -2 * Math.Atan(centre));
        return ForwardBackward(samples, sampleRate, lowCutoff,
            [BandSection(halfWidthPole + root, centreDelay), BandSection(halfWidthPole - root, centreDelay)]);
    }

    private static Section BandSection(Complex analogPole, Complex delay)
    {
        var pole = (1 + analogPole) / (1 - analogPole);
        var a1 = -2 * pole.Real;
        var a2 = pole.Real * pole.Real + pole.Imaginary * pole.Imaginary;
        // Each conjugate pole pair has zeros at DC and Nyquist and unit gain at band centre.
        var b = Complex.Abs((1 + a1 * delay + a2 * delay * delay) / (1 - delay * delay));
        return new Section(b, 0, -b, a1, a2);
    }

    private static void ValidateFrequency(double frequency, double sampleRate)
    {
        if (!double.IsFinite(sampleRate) || sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!double.IsFinite(frequency) || frequency <= 0 || frequency >= sampleRate / 2)
            throw new ArgumentOutOfRangeException(nameof(frequency));
    }

    private static double[] ForwardBackward(ReadOnlySpan<double> samples, double sampleRate, double lowestCutoff,
        ReadOnlySpan<Section> sections)
    {
        if (samples.IsEmpty) return [];
        var pad = (int)Math.Min(samples.Length - 1, Math.Ceiling(sampleRate / lowestCutoff));
        var data = new double[checked(samples.Length + 2 * pad)];
        samples.CopyTo(data.AsSpan(pad));
        for (var i = 1; i <= pad; i++)
        {
            data[pad - i] = 2 * samples[0] - samples[i];
            data[pad + samples.Length - 1 + i] = 2 * samples[^1] - samples[^(i + 1)];
        }

        foreach (var section in sections) section.Apply(data);
        Array.Reverse(data);
        foreach (var section in sections) section.Apply(data);
        Array.Reverse(data);
        return data.AsSpan(pad, samples.Length).ToArray();
    }

    private readonly record struct Section(double B0, double B1, double B2, double A1, double A2)
    {
        public void Apply(Span<double> data)
        {
            var steady = data[0] * (B0 + B1 + B2) / (1 + A1 + A2);
            var z1 = steady - B0 * data[0];
            var z2 = B2 * data[0] - A2 * steady;
            foreach (ref var sample in data)
            {
                var output = B0 * sample + z1;
                z1 = B1 * sample - A1 * output + z2;
                z2 = B2 * sample - A2 * output;
                sample = output;
            }
        }
    }
}
