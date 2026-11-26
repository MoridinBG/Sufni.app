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

    private static IReadOnlyList<string> FrameLines(SetupTerrainScale scale) =>
        TerrainSourceText.ScaleLines(scale, sensor => sensor == TerrainSensor.FrameImu);

    private static SetupTerrainScale Scale(SensorScale frame, TerrainLabelCoverage labels) =>
        new(frame, NotEnoughData, NotEnoughData, NotEnoughData, labels);
}
