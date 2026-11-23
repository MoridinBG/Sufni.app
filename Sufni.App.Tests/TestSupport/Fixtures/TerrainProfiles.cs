using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Fixtures;

public static class TerrainProfiles
{
    public static TerrainProfile Classified(params TerrainWindow[] windows) =>
        new([.. windows], [], [], TerrainRoughnessState.Classified, TerrainSteepnessState.Available, 4096, 4096);

    public static TerrainWindow Window(double start, RoughnessClass roughness, RoughnessSource source, double grade = 0)
    {
        var sensor = new SensorRoughness(2, 1, 1, 1, 2, 0.5, 0);
        return new(10 * start, start, start + 1, 10, true)
        {
            Roughness = roughness, Source = source, Grade = grade, Steepness = SteepnessClass.Gentle,
            Frame = source == RoughnessSource.LowersOnly ? null : sensor,
            Lowers = source == RoughnessSource.FrameOnly ? null : sensor
        };
    }
}
