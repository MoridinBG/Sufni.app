using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Queries;

public class TerrainSelectionQueryTests
{
    [Fact]
    public void RoughSectionsStaySeparateAndPartialWindowsContributeOnlyTheirSelectedDistance()
    {
        var profile = TerrainProfiles.Classified(
            TerrainProfiles.Window(0, RoughnessClass.Calm, RoughnessSource.Both),
            TerrainProfiles.Window(1, RoughnessClass.Rough, RoughnessSource.Both),
            TerrainProfiles.Window(2, RoughnessClass.Calm, RoughnessSource.Both),
            TerrainProfiles.Window(3, RoughnessClass.Rough, RoughnessSource.Both));
        var rough = new TerrainClassFilter().Toggle(RoughnessClass.Rough);
        var ride = TerrainSelectionQuery.Evaluate(profile, rough, null);
        Assert.Equal([new(1, 2), new(3, 4)], ride.Selection!.Ranges);
        Assert.Equal(20, ride.SelectedMetres);
        var range = new TelemetryTimeRange(0, 1.25);
        var partial = TerrainSelectionQuery.Evaluate(profile, rough, range);
        Assert.Equal(new TelemetryTimeRange(1, 1.25), Assert.Single(partial.Selection!.Ranges));
        Assert.Equal(2.5, partial.SelectedMetres);
        Assert.Equal(2.5, partial.RoughnessMetres[(int)RoughnessClass.Rough]);
        Assert.Empty(TerrainSelectionQuery.Evaluate(profile, rough.Toggle(SteepnessClass.Steep), null).Selection!.Ranges);
        Assert.Equal(range, Assert.Single(TerrainSelectionQuery.Evaluate(profile, default, range).Selection!.Ranges));
        Assert.Null(TerrainSelectionQuery.Evaluate(profile, default, null).Selection);
    }
}
