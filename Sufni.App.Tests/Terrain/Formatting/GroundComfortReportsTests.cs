using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;
using static Sufni.App.Tests.TestSupport.Fixtures.TestResponseRows;

namespace Sufni.App.Tests.Terrain.Formatting;

public class GroundComfortReportsTests
{
    [Theory]
    [InlineData(TerrainPreset.Descents, RoughnessClass.Rough)]
    [InlineData(TerrainPreset.Climbs, RoughnessClass.TechnicalClimb)]
    public void LevelsFollowTheScope(TerrainPreset preset, RoughnessClass level)
    {
        var report = GroundComfortReports.Build(Ride(RoughnessSource.Both), new TerrainScope(preset), null, null);

        Assert.Equal(level, Assert.Single(report.Levels).Class);
    }

    [Fact]
    public void ActivityAgainstYourUsualAppearsOnlyWithAUsual()
    {
        var scope = new TerrainScope(TerrainPreset.Descents);

        var withUsual = GroundComfortReports.Build(Ride(RoughnessSource.Both), scope, null, Usual()).Levels.Single();
        var without = GroundComfortReports.Build(Ride(RoughnessSource.Both), scope, null, null);

        Assert.True(withUsual.Fork.HasUsual);
        Assert.False(without.Levels.Single().Fork.HasUsual);
        Assert.True(without.HasUsualNote);
    }

    [Theory]
    [InlineData(RoughnessSource.Both, false)]
    [InlineData(RoughnessSource.FrameOnly, true)]
    [InlineData(RoughnessSource.ForkOnly, true)]
    public void AMissingImuIsNamed(RoughnessSource source, bool noted)
    {
        var report = GroundComfortReports.Build(Ride(source), new TerrainScope(TerrainPreset.All), null, Usual());

        Assert.Equal(noted, report.HasFrameNote);
    }

    private static TerrainProfile Ride(RoughnessSource source) => TerrainProfiles.Classified([
        .. Enumerable.Range(0, 30).Select(i => TerrainProfiles.Window(i, RoughnessClass.Rough, source) with
        {
            Front = new SuspensionWindowFigures(140, 150, 105, 40, null)
        }),
        .. Enumerable.Range(30, 10).Select(i => TerrainProfiles.Window(i, RoughnessClass.TechnicalClimb, source) with { Band = GradeBand.Climb })
    ]);
}
