using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class TerrainPresentationShadeTests
{
    [Fact]
    public void RoughnessShadesHoldTheClassValueInQuarterClassSteps()
    {
        var presentation = RecordedSessionPresentationDeriver.CreateTerrain(TerrainProfiles.Classified(
            Descent(0, 0.55), Descent(1, 0.6), Descent(2, 0.7), TerrainProfiles.Climb(3), Descent(4, 3.4)));
        Assert.Equal([new TerrainShade<RoughnessClass>(0, 2, RoughnessClass.Medium, 0.5), new(2, 3, RoughnessClass.Medium, 0.75),
            new(3, 4, RoughnessClass.SmoothClimb, null), new(4, 5, RoughnessClass.VeryRough, 2.5)], presentation.RoughnessShades);
    }

    [Fact]
    public void LabelledWindowHasAShadeWithoutValue()
    {
        var profile = TerrainProfiles.Profile([Descent(0, 0.5)]);
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.Rough));

        var shades = RecordedSessionPresentationDeriver.CreateTerrain(TerrainClassifier.Classify(profile, TerrainProfiles.Scale, labels)).RoughnessShades;

        Assert.Equal([new TerrainShade<RoughnessClass>(0, 1, RoughnessClass.Rough, null)], shades);
    }

    [Fact]
    public void GradeShadesHoldTheBaselineGradeInWholeDegrees()
    {
        var windows = Enumerable.Range(0, 10).Select(i => TerrainProfiles.Window(i) with
        {
            Grade = Math.Sin((i < 5 ? 3.4 : 25) * Math.PI / 180),
            Band = i < 5 ? GradeBand.GentleDescent : GradeBand.VerySteepDescent
        }).ToArray();
        var shades = RecordedSessionPresentationDeriver.CreateTerrain(TerrainProfiles.Classified(windows)).GradeShades;
        // The median baseline keeps 3.4° up to the last gentle window.
        Assert.Equal(new TerrainShade<GradeBand>(0, 5, GradeBand.GentleDescent, 3), shades[0]);
        // The shade clamps the median of 25° to 17.5° and rounds it to 18°.
        Assert.Equal(new TerrainShade<GradeBand>(5, 10, GradeBand.VerySteepDescent, 18), shades[^1]);
    }

    private static TerrainWindow Descent(int start, double classValue) => TerrainProfiles.Window(start) with
    {
        Band = GradeBand.Flat,
        Frame = TerrainProfiles.Sensor(TerrainLevels.Median(0) * Math.Pow(TerrainLevels.Step, classValue - 0.5))
    };
}
