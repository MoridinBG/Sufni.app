using Sufni.App.Terrain.Formatting;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Formatting;

public class ClimbingReportsTests
{
    [Fact]
    public void ClimbsWithoutEnoughPedallingGiveNoReport()
    {
        var profile = TerrainProfiles.Profile(TerrainProfiles.PedalledClimb(0, 100));

        Assert.Null(ClimbingReports.Build(TerrainClassifier.Classify(profile), null));
    }

    [Fact]
    public void ForkTravelAndClimbGroundAppearOnlyWhenMeasured()
    {
        var measured = TerrainProfiles.Profile(TerrainProfiles.PedalledClimb(0, 120)
            .Select(w => w with { Front = new SuspensionWindowFigures(100, 1, 1, 30, null) }));
        var unmeasured = TerrainProfiles.Profile(TerrainProfiles.PedalledClimb(0, 120).Select(w => w with { Roughness = null }));

        var full = ClimbingReports.Build(TerrainClassifier.Classify(measured), null)!;
        var partial = ClimbingReports.Build(TerrainClassifier.Classify(unmeasured), null)!;

        Assert.True(full.HasForkTravel && full.HasGround);
        Assert.False(partial.HasForkTravel || partial.HasGround);
        Assert.True(partial.HasShockTravel);
    }
}
