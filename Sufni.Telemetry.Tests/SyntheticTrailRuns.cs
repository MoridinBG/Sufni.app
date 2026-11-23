namespace Sufni.Telemetry.Tests;

public static class SyntheticTrailRuns
{
    public static TerrainProfile Record(int groundSeed, double leadInMetres = 0, double roughRebound = 1,
        double calmSpeedFactor = 1, int recordingSeed = 23, bool frame = true, bool lowers = true, bool variedGround = true)
    {
        var random = new Random(groundSeed);
        var sections = new List<RideSection>();
        if (leadInMetres > 0) sections.Add(new(leadInMetres, 7));
        for (var block = 0; block < 12; block++)
        {
            var rough = block % 2 != 0;
            for (var i = 0; i < 12; i++)
            {
                var intensity = variedGround ? (rough ? 2.2 : 0.3) * Math.Exp(random.NextDouble() - 0.5) : 1;
                sections.Add(new(10, rough ? 7 : 7 * calmSpeedFactor, rough ? 12 : 4,
                    intensity * 0.7, intensity, intensity * 0.85, ReboundGain: rough ? roughRebound : 1));
            }
        }
        var ride = SyntheticRide.Build(sections, new RideRecording(Seed: recordingSeed, Frame: frame, Lowers: lowers,
            SurfaceVariation: variedGround ? 0.05 : 0, NoiseG: 0.002, FixRate: 5));
        return ride.Profile();
    }

    public static (double SourceSeconds, double MatchSeconds)[] Map(TerrainProfile source, TerrainProfile match)
    {
        var count = Math.Min(source.Windows.Length, match.Windows.Length);
        return [.. Enumerable.Range(0, count).Select(i => (source.Windows[i].StartSeconds, match.Windows[i].StartSeconds)),
            (source.Windows[count - 1].EndSeconds, match.Windows[count - 1].EndSeconds)];
    }
}
