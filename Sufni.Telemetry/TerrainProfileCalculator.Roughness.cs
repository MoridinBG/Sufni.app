using System.Collections.Immutable;

namespace Sufni.Telemetry;

public static partial class TerrainProfileCalculator
{
    private static readonly (double Low, double High)[] Bands = [(1, 80), (1, 5), (5, 20), (20, 80)];

    private static ImmutableArray<TerrainMatchSample> MeasureSensor(RawImuData? imu, ImuMetaEntry? meta,
        List<TerrainWindow> windows, List<MatchingInterval> positions, CancellationToken token)
    {
        if (imu is null || meta is null) return [];
        var matching = ImmutableArray.CreateBuilder<TerrainMatchSample>();
        var frame = meta.LocationId == (byte)ImuLocation.Frame;
        var calibration = TerrainCalibration.For((ImuLocation)meta.LocationId, meta.AccelLsbPerG);
        var exponent = frame ? TerrainCalibration.FrameSpeedExponent : TerrainCalibration.LowersSpeedExponent;
        foreach (var segment in imu.SampleSegments)
        {
            if (segment.LocationId != meta.LocationId || segment.Count == 0) continue;
            var origin = segment.FirstMonotonicDeltaUs / 1e6;
            var clipped = new int[segment.Count + 1];
            for (var i = 0; i < segment.Count; i++)
            {
                var s = segment[i];
                var peak = Math.Max(Math.Abs((int)s.Ax), Math.Max(Math.Abs((int)s.Ay), Math.Abs((int)s.Az)));
                clipped[i + 1] = clipped[i] + (peak >= TerrainCalibration.FullScaleCount ? 1 : 0);
            }
            var levels = new double[Bands.Length, windows.Count];
            for (var band = 0; band < Bands.Length; band++)
            {
                var power = IntegratedPower(segment, imu.SampleRate, meta.AccelLsbPerG, Bands[band], token);
                double Rms(int a, int b) => Math.Sqrt(Math.Max(0, (power[b] - power[a]) / (b - a)));
                for (var i = 0; i < windows.Count; i++)
                {
                    var w = windows[i];
                    if (!w.Moving || !SampleRange(w.StartSeconds, w.EndSeconds, origin, imu.SampleRate, segment.Count,
                            out var a, out var b)) continue;
                    levels[band, i] = Rms(a, b);
                    if (band == Bands.Length - 1)
                    {
                        var scaled = levels[0, i] * Math.Pow(TerrainCalibration.ReferenceSpeed / w.MeanSpeed, exponent);
                        var measured = new SensorRoughness(levels[0, i], levels[1, i], levels[2, i], levels[3, i],
                            scaled, calibration?.Normalise(scaled), (double)(clipped[b] - clipped[a]) / (b - a));
                        windows[i] = frame ? w with { Frame = measured } : w with { Lowers = measured };
                    }
                }
                if (band != 0) continue;
                foreach (var m in positions)
                    if (SampleRange(m.Start, m.End, origin, imu.SampleRate, segment.Count, out var a, out var b) && Rms(a, b) is > 0 and var level)
                        matching.Add(new(m.Metres, m.Seconds, Math.Log(level)));
            }
        }
        return matching.ToImmutable().Sort((a, b) => a.Metres.CompareTo(b.Metres));
    }

    private static double[] IntegratedPower(ImuSampleSegment segment, int rate, double scale,
        (double Low, double High) band, CancellationToken token)
    {
        var power = new double[segment.Count + 1];
        var axis = new double[segment.Count];
        for (var component = 0; component < 3; component++)
        {
            token.ThrowIfCancellationRequested();
            for (var i = 0; i < axis.Length; i++)
            {
                var s = segment[i];
                axis[i] = (component == 0 ? s.Ax : component == 1 ? s.Ay : s.Az) / scale;
            }
            var filtered = SignalFilters.BandPass(axis, rate, band.Low, band.High);
            for (var i = 0; i < axis.Length; i++) power[i + 1] += filtered[i] * filtered[i];
        }
        for (var i = 1; i < power.Length; i++) power[i] += power[i - 1];
        return power;
    }

    private static (RoughnessClass?, RoughnessSource?) Classify(SensorRoughness? frame, SensorRoughness? lowers)
    {
        var fork = frame != null && lowers?.ClippedShare > TerrainCalibration.ClippedShareLimit ? null : lowers?.Normalised;
        var (value, source) = (frame?.Normalised, fork) switch
        {
            ({ } a, { } b) => ((a + b) / 2, RoughnessSource.Both),
            ({ } a, null) => (a, RoughnessSource.FrameOnly),
            (null, { } b) => (b, RoughnessSource.LowersOnly),
            _ => (double.NaN, default)
        };
        if (double.IsNaN(value)) return (null, null);
        return (TerrainCalibration.Classify(value), source);
    }
}
