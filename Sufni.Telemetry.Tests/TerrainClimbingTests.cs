namespace Sufni.Telemetry.Tests;

public class TerrainClimbingTests
{
    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    public void ClimbsNeedTwoMinutesOfDetectedPedalling(int pedalledWindows, bool described)
    {
        var profile = TerrainProfiles.Profile(TerrainProfiles.PedalledClimb(0, pedalledWindows, 8));

        Assert.Equal(described, TerrainClimbing.Describe(TerrainClassifier.Classify(profile, TerrainProfiles.Scale), null) is not null);
    }

    [Fact]
    public void PedallingOutsideTheRangeDoesNotCount()
    {
        var profile = TerrainProfiles.Profile(TerrainProfiles.PedalledClimb(0, 200, 8));

        Assert.Null(TerrainClimbing.Describe(TerrainClassifier.Classify(profile, TerrainProfiles.Scale), new(0, 100)));
    }

    [Fact]
    public void SummaryMeasuresTheClimbsAndTakesBobAndCadenceFromPedalledWindowsOnly()
    {
        var technical = Enumerable.Range(160, 40).Select(i => TerrainProfiles.Climb(i) with
        {
            Frame = TerrainProfiles.Sensor(TerrainProfiles.Level(RoughnessClass.TechnicalClimb))
        });
        var profile = TerrainProfiles.Profile([.. TerrainProfiles.PedalledClimb(0, 100, 7), .. TerrainProfiles.PedalledClimb(100, 60, 9), .. technical]);

        var climbing = TerrainClimbing.Describe(TerrainClassifier.Classify(profile, TerrainProfiles.Scale), null)!;

        Assert.Equal(2000, climbing.DistanceMetres, 6);
        Assert.Equal(200, climbing.Seconds, 6);
        Assert.Equal(200, climbing.AscentMetres, 6);
        Assert.Equal(0.8, climbing.PedalledShare, 6);
        Assert.Equal(7, climbing.BobMillimetres);
        Assert.Equal((1600d, 400d), (climbing.SmoothMetres, climbing.TechnicalMetres));
    }

    [Fact]
    public void TravelComparesClimbsWithDescentBandsAndLeavesOutFlatGround()
    {
        var descent = TerrainProfiles.Window(120) with { Band = GradeBand.Descent, Rear = Travel(30) };
        var flat = TerrainProfiles.Window(121) with { Band = GradeBand.Flat, Rear = Travel(80) };
        var profile = TerrainProfiles.Profile([.. TerrainProfiles.PedalledClimb(0, 120, 8), descent, flat]);

        var climbing = TerrainClimbing.Describe(TerrainClassifier.Classify(profile, TerrainProfiles.Scale), null)!;

        Assert.Equal(new ClimbTravel(50, 30), climbing.Rear);
        Assert.Equal(new ClimbTravel(null, null), climbing.Front);
    }

    private static SuspensionWindowFigures Travel(double meanTravel) => new(100, 1, 1, meanTravel, null);
}
