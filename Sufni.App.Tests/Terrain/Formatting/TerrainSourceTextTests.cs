using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Terrain.Formatting;

public class TerrainSourceTextTests
{
    private static readonly SensorScale NotEnoughData = new SensorScale.NotEnoughData(1, 900);

    [Fact]
    public void EachScaleStateGivesItsOwnLine()
    {
        SensorScale[] states = [new SensorScale.Ready(3), new SensorScale.Anchored(3), NotEnoughData];

        var lines = states.Select(state => Assert.Single(FrameLines(Scale(state, TerrainLabelCoverage.None)))).ToArray();

        Assert.Equal(states.Length, lines.Distinct().Count());
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void LabelledDistanceAddsALineOnlyWithoutAnAnchor(bool anchored, int count)
    {
        SensorScale state = anchored ? new SensorScale.Anchored(3) : new SensorScale.Ready(3);

        Assert.Equal(count, FrameLines(Scale(state, new TerrainLabelCoverage(40, 0, 0, 0, 0))).Count);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void ClimbLabelsAddALineOnlyWithoutAClimbAnchor(bool anchored, int count)
    {
        SensorScale state = anchored ? new SensorScale.Anchored(3) : NotEnoughData;
        var scale = new SetupTerrainScale(NotEnoughData, NotEnoughData, NotEnoughData, state, new TerrainLabelCoverage(0, 0, 0, 120, 30));

        Assert.Equal(count, TerrainSourceText.ScaleLines(scale, sensor => sensor is null).Count);
    }

    [Fact]
    public void LinesFollowTheAdmittedSensors()
    {
        var scale = new SetupTerrainScale(NotEnoughData, NotEnoughData, NotEnoughData, NotEnoughData, TerrainLabelCoverage.None);

        Assert.Equal(4, TerrainSourceText.ScaleLines(scale, _ => true).Count);
        Assert.Single(TerrainSourceText.ScaleLines(scale, sensor => sensor == TerrainSensor.ForkImu));
    }

    [Fact]
    public void DescriptionHoldsTheScaleLinesOfTheSensorsWithReadingsInTheRide()
    {
        var presentation = RecordedSessionPresentationDeriver.CreateTerrain(TerrainProfiles.Classified(TerrainProfiles.Window(0, RoughnessClass.Rough)));
        var scale = new SetupTerrainScale(new SensorScale.Anchored(3), NotEnoughData, NotEnoughData, NotEnoughData,
            new TerrainLabelCoverage(120, 0, 0, 0, 0));

        var description = TerrainSourceText.Description(presentation, scale)!.Split('\n');

        Assert.Contains(Assert.Single(FrameLines(scale)), description);
        Assert.DoesNotContain(Assert.Single(TerrainSourceText.ScaleLines(scale, sensor => sensor == TerrainSensor.ForkImu)), description);
    }

    [Fact]
    public void LabelledShareAddsALine()
    {
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0, RoughnessClass.Rough), TerrainProfiles.Window(1, RoughnessClass.Rough)]);
        var labels = TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.Calm));
        string[] Lines(TerrainLabelSet set) =>
            TerrainSourceText.Description(RecordedSessionPresentationDeriver.CreateTerrain(TerrainClassifier.Classify(profile, TerrainProfiles.Scale, set)), null)!
                .Split('\n');

        Assert.Equal(Lines(TerrainLabelSet.None).Length + 1, Lines(labels).Length);
    }

    [Theory]
    [InlineData(GroundSensors.Frame, true)]
    [InlineData(GroundSensors.Fork, false)]
    [InlineData(GroundSensors.RearAxle, false)]
    [InlineData(GroundSensors.ForkAndRearAxle, false)]
    public void OnlyTheFrameIsASprungSource(GroundSensors source, bool sprung)
    {
        var presentation = RecordedSessionPresentationDeriver.CreateTerrain(TerrainProfiles.Classified(
            TerrainProfiles.Window(0, RoughnessClass.Rough, source)));

        Assert.Equal(sprung, TerrainSourceText.IsSprung(presentation.Coverage));
    }

    [Fact]
    public void ARideWithoutClassesIsNotSprung()
    {
        var presentation = RecordedSessionPresentationDeriver.CreateTerrain(TerrainProfiles.Classified(TerrainProfiles.Window(0)));

        Assert.False(TerrainSourceText.IsSprung(presentation.Coverage));
        Assert.False(TerrainSourceText.IsSprung(null));
    }

    [Fact]
    public void AFrameMajorityAddsTheSprungLine()
    {
        // Both rides mix the two sources, so they differ only in the source of the largest share.
        string[] Lines(GroundSensors majority, GroundSensors minority) =>
            TerrainSourceText.Description(RecordedSessionPresentationDeriver.CreateTerrain(TerrainProfiles.Classified(
                TerrainProfiles.Window(0, RoughnessClass.Rough, majority),
                TerrainProfiles.Window(1, RoughnessClass.Rough, majority),
                TerrainProfiles.Window(2, RoughnessClass.Rough, minority))), null)!.Split('\n');

        Assert.Equal(Lines(GroundSensors.Fork, GroundSensors.Frame).Length + 1, Lines(GroundSensors.Frame, GroundSensors.Fork).Length);
    }

    private static IReadOnlyList<string> FrameLines(SetupTerrainScale scale) =>
        TerrainSourceText.ScaleLines(scale, sensor => sensor == TerrainSensor.FrameImu);

    private static SetupTerrainScale Scale(SensorScale frame, TerrainLabelCoverage labels) =>
        new(frame, NotEnoughData, NotEnoughData, NotEnoughData, labels);
}
