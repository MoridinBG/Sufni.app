using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class TerrainPresentationLabelRunTests
{
    [Fact]
    public void OverlappingLabelsGiveRunsThatDoNotOverlapAndTheOverlapKeepsTheFirstLabel()
    {
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 10, RoughnessClass.Rough),
            new TerrainLabelRange(2, 5, RoughnessClass.Calm), new TerrainLabelRange(4, 12, RoughnessClass.VeryRough));
        var classified = TerrainClassifier.Classify(TerrainProfiles.Profile([TerrainProfiles.Window(0)]), TerrainProfiles.Scale, labels);

        var runs = RecordedSessionPresentationDeriver.CreateTerrain(classified).LabelRuns;

        Assert.Equal([new TerrainRun<RoughnessClass>(0, 10, RoughnessClass.Rough), new(10, 12, RoughnessClass.VeryRough)], runs);
    }
}
