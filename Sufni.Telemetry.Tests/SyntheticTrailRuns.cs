using System.Collections.Immutable;
using MathNet.Numerics.Distributions;

namespace Sufni.Telemetry.Tests;

/// <summary>A trail section as one recording rides it: the frame level in g at 7 m/s and the class both sensors give it.</summary>
public readonly record struct TrailSection(double StartMetres, double FrameLevel, RoughnessClass Roughness);

/// <summary>
/// Runs over synthetic trails modelled on the pooled statistics of real descents. The ground seed lays out stretches of
/// distinct character with short texture on top, at fixed class shares under the production calibration. The recording
/// seed adds the run's own line and surface condition, so repeat runs correlate as real repeats do.
/// </summary>
public static class SyntheticTrailRuns
{
    public const double TrailMetres = 2400;
    public const double SectionMetres = 2;
    public const double CalmShare = 0.45;
    public const double RoughShare = 0.30;

    private const double CharacterMetres = 100;
    private const double CharacterVariance = 0.75;
    private const double TextureMetres = 5;
    private const double RunVariation = 0.3;
    private const double Speed = 7;
    private const double GradeDegrees = 8;
    private const double LowBand = 0.7, MiddleBand = 1, HighBand = 0.85;

    private static readonly double BandLevel = Math.Sqrt(LowBand * LowBand + MiddleBand * MiddleBand + HighBand * HighBand);
    private static readonly int AccelScale = new RideRecording().AccelScale;
    private static readonly TerrainCalibration FrameCalibration = TerrainCalibration.For(ImuLocation.Frame, AccelScale)!;
    private static readonly TerrainCalibration LowersCalibration = TerrainCalibration.For(ImuLocation.Fork, AccelScale)!;

    public static ImmutableArray<TrailSection> Describe(int groundSeed, int recordingSeed = 23)
    {
        var count = (int)(TrailMetres / SectionMetres);
        var ground = new Random(groundSeed);
        var character = 0d;
        var remaining = 0d;
        var texture = Normal.Sample(ground, 0, 1);
        var layout = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (remaining <= 0)
            {
                character = Normal.Sample(ground, 0, 1);
                remaining = Exponential.Sample(ground, 1 / CharacterMetres);
            }
            remaining -= SectionMetres;
            texture = Continue(texture, ground);
            layout[i] = Math.Sqrt(CharacterVariance) * character + Math.Sqrt(1 - CharacterVariance) * texture;
        }

        // Ranking fixes the class shares for every seed; the seed only decides where each class lies.
        var calm = LogLevelAt(0);
        var spread = (LogLevelAt(1) - calm) / (Normal.InvCDF(0, 1, 1 - RoughShare) - Normal.InvCDF(0, 1, CalmShare));
        var ranks = new int[count];
        foreach (var (index, rank) in layout.Select((value, index) => (value, index)).OrderBy(p => p.value).Select((p, rank) => (p.index, rank)))
            ranks[index] = rank;
        var run = new Random(unchecked(groundSeed * 7919 + recordingSeed));
        var variation = Normal.Sample(run, 0, 1);
        var sections = ImmutableArray.CreateBuilder<TrailSection>(count);
        for (var i = 0; i < count; i++)
        {
            variation = Continue(variation, run);
            var quantile = Normal.InvCDF(0, 1, (ranks[i] + 0.5) / count) - Normal.InvCDF(0, 1, CalmShare);
            var level = Math.Exp(calm + spread * quantile + RunVariation * variation);
            sections.Add(new(i * SectionMetres, level, TerrainCalibration.Classify(Normalised(level))));
        }
        return sections.MoveToImmutable();
    }

    public static TerrainProfile Record(int groundSeed, double leadInMetres = 0, double roughRebound = 1,
        double calmSpeedFactor = 1, int recordingSeed = 23, bool frame = true, bool lowers = true)
    {
        var sections = new List<RideSection>();
        if (leadInMetres > 0) sections.Add(new(leadInMetres, Speed, GradeDegrees));
        foreach (var section in Describe(groundSeed, recordingSeed))
        {
            var intensity = section.FrameLevel / BandLevel;
            sections.Add(new(SectionMetres, section.Roughness <= RoughnessClass.Calm ? Speed * calmSpeedFactor : Speed, GradeDegrees,
                intensity * LowBand, intensity * MiddleBand, intensity * HighBand,
                ReboundGain: section.Roughness >= RoughnessClass.Rough ? roughRebound : 1));
        }
        var ride = SyntheticRide.Build(sections, new RideRecording(Seed: recordingSeed, Frame: frame, Lowers: lowers,
            NoiseG: 0.002, FixRate: 5));
        return ride.Profile();
    }

    public static (double SourceSeconds, double MatchSeconds)[] Map(TerrainProfile source, TerrainProfile match)
    {
        var count = Math.Min(source.Windows.Length, match.Windows.Length);
        return [.. Enumerable.Range(0, count).Select(i => (source.Windows[i].StartSeconds, match.Windows[i].StartSeconds)),
            (source.Windows[count - 1].EndSeconds, match.Windows[count - 1].EndSeconds)];
    }

    private static double Continue(double value, Random random)
    {
        var decay = Math.Exp(-SectionMetres / TextureMetres);
        return decay * value + Math.Sqrt(1 - decay * decay) * Normal.Sample(random, 0, 1);
    }

    private static double Normalised(double frameLevel) => (FrameCalibration.Normalise(frameLevel) +
        LowersCalibration.Normalise(SyntheticRide.LowersLevelFactor * frameLevel)) / 2;

    // The mean normalised value is linear in the log level, so two points give the log level of any class boundary.
    private static double LogLevelAt(double normalised)
    {
        var atOne = Normalised(1);
        return (normalised - atOne) / (Normalised(Math.E) - atOne);
    }
}
