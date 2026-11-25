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
        var report = GroundComfortReports.Build(Ride(), new TerrainScope(preset), null, null);

        Assert.Equal(level, Assert.Single(report.Levels).Class);
    }

    [Fact]
    public void ActivityAgainstYourUsualAppearsOnlyWithAUsual()
    {
        var scope = new TerrainScope(TerrainPreset.Descents);

        var withUsual = GroundComfortReports.Build(Ride(), scope, null, Usual()).Levels.Single();
        var without = GroundComfortReports.Build(Ride(), scope, null, null);

        Assert.True(withUsual.Fork.HasUsual);
        Assert.False(without.Levels.Single().Fork.HasUsual);
        Assert.True(without.HasUsualNote);
    }

    [Theory]
    [InlineData(GroundSensors.Fork, true, false)]
    [InlineData(GroundSensors.RearAxle, true, false)]
    [InlineData(GroundSensors.Frame, true, true)]
    [InlineData(GroundSensors.Fork, false, true)]
    public void AMissingImuIsNamed(GroundSensors source, bool frame, bool noted)
    {
        var report = GroundComfortReports.Build(Ride(source, frame), new TerrainScope(TerrainPreset.All), null, Usual());

        Assert.Equal(noted, report.HasFrameNote);
    }

    private static ClassifiedProfile Ride(GroundSensors source = GroundSensors.Fork, bool frame = true) => TerrainProfiles.Classified([
        .. Enumerable.Range(0, 30).Select(i => TerrainProfiles.Window(i, RoughnessClass.Rough, source) with
        {
            Front = new SuspensionWindowFigures(140, 150, 105, 40, null),
            Frame = frame ? TerrainProfiles.Sensor(TerrainProfiles.Level(RoughnessClass.Rough)) : null
        }),
        .. Enumerable.Range(30, 10).Select(i => TerrainProfiles.Window(i, RoughnessClass.TechnicalClimb, source) with
        {
            Band = GradeBand.Climb,
            Frame = frame ? TerrainProfiles.Sensor(TerrainProfiles.Level(RoughnessClass.TechnicalClimb)) : null
        })
    ]);
}
