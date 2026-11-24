namespace Sufni.Telemetry.Tests;

public static class TerrainProfiles
{
    public static TerrainProfile Classified(params TerrainWindow[] windows) => Profile(windows);

    public static TerrainProfile Profile(IEnumerable<TerrainWindow> windows,
        TerrainRoughnessState roughness = TerrainRoughnessState.Classified,
        TerrainGradeState grade = TerrainGradeState.Available, double? frameScale = 4096, double? lowersScale = 4096) =>
        new([.. windows], [], [], roughness, grade, frameScale, lowersScale);

    /// <summary>The ten-metre window that starts at <paramref name="start"/> seconds and lasts one second.</summary>
    public static TerrainWindow Window(double start, bool moving = true) => new(10 * start, start, start + 1, 10, moving);

    public static TerrainWindow Window(double start, RoughnessClass roughness, RoughnessSource source, double grade = 0)
    {
        var sensor = new SensorRoughness(2, 1, 1, 1, 2, 0.5, 0);
        return Window(start) with
        {
            Roughness = roughness, Source = source, Grade = grade, Band = GradeBand.Flat,
            Frame = source == RoughnessSource.LowersOnly ? null : sensor,
            Lowers = source == RoughnessSource.FrameOnly ? null : sensor
        };
    }

    /// <summary>A sensor reading with every band and the speed-removed value at <paramref name="level"/>.</summary>
    public static SensorRoughness Sensor(double level, double? normalised = null, double clippedShare = 0) =>
        new(level, level, level, level, level, normalised, clippedShare);

    /// <summary>Pairs each of the first <paramref name="windows"/> windows with the window of the same index.</summary>
    public static TerrainRunAlignment.Aligned Identity(int windows) =>
        new(ImuLocation.Frame, [], [.. Enumerable.Range(0, windows).Select(i => new TerrainWindowPair(i, i))]);
}
