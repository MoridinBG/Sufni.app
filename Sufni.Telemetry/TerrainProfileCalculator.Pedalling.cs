using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    private const double RhythmRate = 50;
    private const double RhythmSeconds = 8;
    private const int RhythmBins = 1024;
    private const int RhythmPeakHalfWidth = 5;
    private const double PedalledRatio = 0.5;
    private const double RearOnlyPedalledRatio = 0.6;

    private readonly record struct Series(double Start, double Rate, double[] Values);
    private readonly record struct Rhythm(double Ratio, double PeakHz);

    // Seated pedalling moves the rear and the frame at one steady rhythm. Ground input can give the rear a
    // rhythm alone, so the frame must agree when it is recorded.
    private static void MeasurePedalling(Suspension rear, int rate, RawImuData? imu, ImuMetaEntry? frame,
        List<TerrainWindow> windows, CancellationToken token)
    {
        if (rear is not { Present: true } || rear.Travel.IsDefaultOrEmpty || rate <= 0) return;
        var segments = SegmentsOf(rear);
        var travel = segments.Select(s => Decimate(s.StartSeconds, rate,
            rear.Travel.AsSpan(s.FirstDenseIndex, s.SampleCount))).ToArray();
        List<Series>? vertical = null;
        if (imu is not null && frame is not null)
        {
            vertical = [];
            foreach (var segment in imu.SampleSegments)
            {
                if (segment.LocationId != frame.LocationId || segment.Count == 0) continue;
                var values = new double[segment.Count];
                for (var i = 0; i < values.Length; i++) values[i] = segment[i].Az / (double)frame.AccelLsbPerG;
                vertical.Add(Decimate(segment.FirstMonotonicDeltaUs / 1e6, imu.SampleRate, values));
            }
        }
        for (var i = 0; i < windows.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var window = windows[i];
            if (!window.Moving) continue;
            var centre = (window.StartSeconds + window.EndSeconds) / 2;
            if (Excerpt(travel, centre) is not { } rearFrame || MeasureRhythm(rearFrame) is not { } rearRhythm) continue;
            bool detected;
            if (vertical is null) detected = rearRhythm.Ratio >= RearOnlyPedalledRatio;
            else if (Excerpt(vertical, centre) is { } frameFrame) detected = rearRhythm.Ratio >= PedalledRatio &&
                MeasureRhythm(frameFrame) is { Ratio: >= PedalledRatio };
            else continue;
            windows[i] = window with
            {
                Pedalling = detected
                    ? new(true, rearRhythm.PeakHz * 30, PeakToPeak(rearFrame))
                    : new(false, null, null)
            };
        }
    }

    private static Series Decimate(double start, double rate, ReadOnlySpan<double> values)
    {
        var block = Math.Max(1, (int)Math.Round(rate / RhythmRate));
        var output = new double[values.Length / block];
        for (var i = 0; i < output.Length; i++)
        {
            double sum = 0;
            for (var j = 0; j < block; j++) sum += values[i * block + j];
            output[i] = sum / block;
        }
        // A block mean belongs to the middle of its block.
        return new(start + (block - 1) / 2d / rate, rate / block, output);
    }

    private static Series? Excerpt(IEnumerable<Series> series, double centre)
    {
        foreach (var s in series)
        {
            var first = (int)Math.Round((centre - RhythmSeconds / 2 - s.Start) * s.Rate);
            var count = (int)Math.Round(RhythmSeconds * s.Rate);
            if (first >= 0 && first + count <= s.Values.Length) return s with { Values = s.Values[first..(first + count)] };
        }
        return null;
    }

    /// <summary>Share of 0.5–6 Hz power in the strongest 1.2–3.6 Hz peak, after a linear detrend and a Hann window.</summary>
    private static Rhythm? MeasureRhythm(Series excerpt)
    {
        var values = excerpt.Values;
        var n = values.Length;
        double centre = (n - 1) / 2d, mean = values.Average(), covariance = 0, spread = 0;
        for (var i = 0; i < n; i++)
        {
            covariance += (i - centre) * (values[i] - mean);
            spread += (i - centre) * (i - centre);
        }
        var slope = covariance / spread;
        var spectrum = new Complex[RhythmBins];
        for (var i = 0; i < n; i++)
            spectrum[i] = (values[i] - mean - slope * (i - centre)) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
        Fourier.Forward(spectrum, FourierOptions.Matlab);
        var hz = excerpt.Rate / RhythmBins;
        double total = 0;
        var peak = -1;
        for (var k = (int)Math.Ceiling(0.5 / hz); k <= 6 / hz; k++)
        {
            var power = spectrum[k].Magnitude * spectrum[k].Magnitude;
            total += power;
            if (k * hz >= 1.2 && k * hz <= 3.6 && (peak < 0 || power > spectrum[peak].Magnitude * spectrum[peak].Magnitude)) peak = k;
        }
        if (!(total > 0) || peak < 0) return null;
        double peakPower = 0;
        for (var k = peak - RhythmPeakHalfWidth; k <= peak + RhythmPeakHalfWidth; k++)
            peakPower += spectrum[k].Magnitude * spectrum[k].Magnitude;
        return new(peakPower / total, peak * hz);
    }

    // The 1–4 Hz travel over the middle half of the excerpt, as the peak-to-peak height of a sine with its RMS.
    private static double PeakToPeak(Series excerpt)
    {
        var band = SignalFilters.BandPass(excerpt.Values, excerpt.Rate, 1, 4);
        var middle = band.AsSpan(band.Length / 4, band.Length / 2);
        double power = 0;
        foreach (var value in middle) power += value * value;
        return 2 * Math.Sqrt(2) * Math.Sqrt(power / middle.Length);
    }
}
