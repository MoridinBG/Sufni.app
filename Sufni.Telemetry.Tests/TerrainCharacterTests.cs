namespace Sufni.Telemetry.Tests;

public class TerrainCharacterTests
{
    [Theory]
    [InlineData(34, 34, StrokeCharacter.BigHits)]
    [InlineData(14, 11, StrokeCharacter.Chatter)]
    [InlineData(24, 20, null)]
    public void MostRoughGroundTakesTheCharacterOfItsStrokeSizeAtEachEnd(double frontMillimetres, double rearMillimetres,
        StrokeCharacter? expected)
    {
        var character = TerrainCharacter.Describe(Ride(40, RoughnessClass.Rough, frontMillimetres, rearMillimetres), null);
        Assert.Equal(expected, character.Front!.Dominant);
        Assert.Equal(expected, character.Rear!.Dominant);
    }

    [Fact]
    public void FrontAndRearUseTheirOwnShareOfTravel()
    {
        // 26 mm is 16% of the 160 mm fork, under its big-hit share, and 17% of the 150 mm shock, over its own.
        var character = TerrainCharacter.Describe(Ride(40, RoughnessClass.Rough, 26, 26), null);
        Assert.Equal(0, character.Front!.BigHitShare);
        Assert.Equal(1, character.Rear!.BigHitShare);
    }

    [Fact]
    public void CalmGroundAndShortRoughGroundGiveNoCharacter()
    {
        Assert.Null(TerrainCharacter.Describe(Ride(40, RoughnessClass.Calm, 34, 34), null).Rear);
        var shortRough = TerrainCharacter.Describe(Ride(29, RoughnessClass.Rough, 34, 34), null).Rear!;
        Assert.Equal(290, shortRough.RoughMetres);
        Assert.Null(shortRough.Dominant);
    }

    [Fact]
    public void TimeRangeLimitsTheGround()
    {
        Assert.Equal(100, TerrainCharacter.Describe(Ride(40, RoughnessClass.Rough, 34, 34), new(10, 20)).Rear!.RoughMetres, 6);
    }

    private static TerrainProfile Ride(int windows, RoughnessClass roughness, double front, double rear) =>
        TerrainProfiles.Classified([.. Enumerable.Range(0, windows).Select(i => TerrainProfiles.Window(i, roughness, RoughnessSource.Both) with
        {
            Front = new SuspensionWindowFigures(1, 1, 1, 40, front),
            Rear = new SuspensionWindowFigures(1, 1, 1, 40, rear)
        })]) with { FrontMaxTravel = 160, RearMaxTravel = 150 };
}
