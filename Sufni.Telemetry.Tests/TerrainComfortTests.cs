namespace Sufni.Telemetry.Tests;

public class TerrainComfortTests
{
    [Fact]
    public void EachLevelTakesTheQuadraticMeanOfItsWindows()
    {
        var profile = TerrainProfiles.Classified(Rough(0, 3, 0.3), Rough(1, 4, 0.4), Calm(2, 1));

        var rows = TerrainComfort.Describe(TerrainClassifier.Classify(profile), null, _ => true);

        var rough = rows.Single(row => row.Class == RoughnessClass.Rough);
        Assert.Equal(20, rough.DistanceMetres, 6);
        Assert.Equal(Math.Sqrt(12.5), rough.FrontActivity!.Value, 6);
        Assert.Equal(Math.Sqrt(0.125), rough.FrameLevel!.Value, 6);
        Assert.Equal([RoughnessClass.Calm, RoughnessClass.Rough], rows.Select(row => row.Class));
    }

    [Fact]
    public void RangeAndFilterLimitTheGround()
    {
        var profile = TerrainProfiles.Classified(Rough(0, 3, 0.3), Rough(1, 4, 0.4), Calm(2, 1));

        var rows = TerrainComfort.Describe(TerrainClassifier.Classify(profile), new(0.5, 3), w => w.Roughness == RoughnessClass.Rough);

        var rough = Assert.Single(rows);
        Assert.Equal(15, rough.DistanceMetres, 6);
    }

    [Fact]
    public void AMissingSensorLeavesItsFigureEmpty()
    {
        var rows = TerrainComfort.Describe(TerrainClassifier.Classify(TerrainProfiles.Classified(Calm(0, 1))), null, _ => true);

        Assert.Null(Assert.Single(rows).RearActivity);
    }

    private static TerrainWindow Rough(int index, double activity, double frame) =>
        TerrainProfiles.Window(index, RoughnessClass.Rough, RoughnessSource.Both) with
        {
            Front = new SuspensionWindowFigures(activity, 1, 1, 40, null),
            Rear = new SuspensionWindowFigures(activity, 1, 1, 40, null),
            Frame = TerrainProfiles.Sensor(frame)
        };

    private static TerrainWindow Calm(int index, double activity) =>
        TerrainProfiles.Window(index, RoughnessClass.Calm, RoughnessSource.Both) with
        {
            Front = new SuspensionWindowFigures(activity, 1, 1, 40, null)
        };
}
