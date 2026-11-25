using System.Collections.Immutable;

namespace Sufni.Telemetry.Tests;

public static class SyntheticResponseRides
{
    private static readonly Lazy<ImmutableArray<TerrainProfile>> baseline = new(() =>
        [.. Enumerable.Range(1, 6).Select(seed => Profile(seed))]);

    public static ImmutableArray<TerrainProfile> Baseline => baseline.Value;

    public static TerrainProfile Profile(int seed, double gain = 1, bool fork = true)
    {
        RideSection[] sections =
        [
            new(440, 5, 3, 0.2, 0.25, 0.3), new(440, 7, 8, 0.7, 0.5, 0.85),
            new(440, 6, 7, 0.8, 1.2, 0.7), new(440, 9, 13, 1.4, 1.3, 2.1),
            new(440, 8, 12, 1.3, 1.7, 1.3)
        ];
        var ride = SyntheticRide.Build(sections.Select(s => s with { CompressionGain = gain, ReboundGain = gain }).ToArray(),
            new(Seed: seed, Fork: fork, NoiseG: 0.002));
        return ride.Profile();
    }
}
