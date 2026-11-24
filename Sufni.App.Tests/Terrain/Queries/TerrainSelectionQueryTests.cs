using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Queries;

public class TerrainSelectionQueryTests
{
    private static readonly TerrainProfile Ride = TerrainProfiles.Classified(
        TerrainProfiles.Window(0, RoughnessClass.Calm, RoughnessSource.Both),
        TerrainProfiles.Window(1, RoughnessClass.Rough, RoughnessSource.Both),
        TerrainProfiles.Window(2, moving: false) with { EndSeconds = 6 },
        TerrainProfiles.Window(6, RoughnessClass.Rough, RoughnessSource.Both) with { Band = null },
        TerrainProfiles.Window(7) with { Band = GradeBand.Climb, Roughness = RoughnessClass.SmoothClimb });

    [Theory]
    [InlineData(TerrainPreset.All, 40)]
    [InlineData(TerrainPreset.Descents, 30)]
    [InlineData(TerrainPreset.Climbs, 10)]
    public void EveryPresetLeavesOutStoppedTime(TerrainPreset preset, double metres)
    {
        var result = TerrainSelectionQuery.Evaluate(Ride, new(preset), null);
        Assert.Equal(metres, result.SelectedMetres);
        Assert.Equal(metres, result.PresetMetres(preset));
        Assert.Equal(4, result.StoppedSeconds);
        Assert.DoesNotContain(result.Selection.Ranges, r => r.StartSeconds < 6 && r.EndSeconds > 2);
    }

    [Fact]
    public void LegendClassesNarrowThePresetAndEmptyGroundSelectsNothing()
    {
        var rough = new TerrainScope(TerrainPreset.Descents).Toggle(RoughnessClass.Rough);
        Assert.Equal([new(1, 2), new(6, 7)], TerrainSelectionQuery.Evaluate(Ride, rough, null).Selection.Ranges);
        Assert.Empty(TerrainSelectionQuery.Evaluate(Ride, rough.Toggle(GradeBand.SteepDescent), null).Selection.Ranges);
    }

    [Fact]
    public void PartialWindowsContributeOnlyTheirSelectedDistance()
    {
        var rough = new TerrainScope(TerrainPreset.All).Toggle(RoughnessClass.Rough);
        var partial = TerrainSelectionQuery.Evaluate(Ride, rough, new TelemetryTimeRange(0, 1.25));
        Assert.Equal(new TelemetryTimeRange(1, 1.25), Assert.Single(partial.Selection.Ranges));
        Assert.Equal(2.5, partial.SelectedMetres);
        Assert.Equal(2.5, partial.RoughnessMetres[(int)RoughnessClass.Rough]);
    }

    [Fact]
    public void ClassesOutsideThePresetCannotBeToggled()
    {
        var descents = new TerrainScope(TerrainPreset.Descents);
        Assert.Equal(descents, descents.Toggle(GradeBand.SteepClimb).Toggle(RoughnessClass.TechnicalClimb));
    }
}
