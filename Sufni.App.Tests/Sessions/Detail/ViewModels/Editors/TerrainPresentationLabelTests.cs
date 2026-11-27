using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class TerrainPresentationLabelTests
{
    [Fact]
    public void OverlappingLabelsGiveRunsThatDoNotOverlapAndTheOverlapKeepsTheFirstLabel()
    {
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 10, RoughnessClass.Rough),
            new TerrainLabelRange(2, 5, RoughnessClass.Calm), new TerrainLabelRange(4, 12, RoughnessClass.VeryRough));
        var classified = TerrainClassifier.Classify(TerrainProfiles.Profile([TerrainProfiles.Window(0)]), TerrainProfiles.Scale, labels);

        var runs = RecordedSessionPresentationDeriver.CreateTerrain(classified).RoughnessLabelRuns;

        Assert.Equal([new TerrainRun<RoughnessClass>(0, 10, RoughnessClass.Rough), new(10, 12, RoughnessClass.VeryRough)], runs);
    }

    [Fact]
    public void GradeLaneShowsTheBandOfASteepnessLabelWithoutAMeasuredShade()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0, RoughnessClass.Rough) with { Band = GradeBand.Descent }]);
        var labels = TerrainProfiles.GradeLabels(new GradeLabelRange(0, 1, GradeBand.SteepDescent));

        var terrain = RecordedSessionPresentationDeriver.CreateTerrain(TerrainClassifier.Classify(profile, TerrainProfiles.Scale, labels));

        Assert.Equal([new TerrainRun<GradeBand>(0, 1, GradeBand.SteepDescent)], terrain.GradeRuns);
        Assert.Null(Assert.Single(terrain.GradeShades).Value);
    }

    [Fact]
    public void LabelledShareIsTheShareOfTheClassedMovingWindowsThatALabelClasses()
    {
        var profile = TerrainProfiles.Profile([.. Enumerable.Range(0, 4).Select(i => TerrainProfiles.Window(i, RoughnessClass.Rough)),
            TerrainProfiles.Window(4, moving: false)]);
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.Calm));

        var coverage = RecordedSessionPresentationDeriver.CreateTerrain(TerrainClassifier.Classify(profile, TerrainProfiles.Scale, labels)).Coverage;

        Assert.Equal(0.25, coverage.LabelledShare);
    }
}
