using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class BikeCharacteristicsRearMappingTests
{
    [Fact]
    public void ShockStrokeToWheelTravelDataset_StartsAtZero_AndIsMonotonic()
    {
        var characteristics = CreateCharacteristics();

        var dataset = characteristics.ShockStrokeToWheelTravelDataset();

        Assert.True(dataset.X.Count > 1);
        Assert.Equal(0, dataset.X[0], 6);
        Assert.Equal(0, dataset.Y[0], 6);
        Assert.All(dataset.X.Zip(dataset.X.Skip(1)), pair => Assert.True(pair.Second >= pair.First));
        Assert.All(dataset.Y.Zip(dataset.Y.Skip(1)), pair => Assert.True(pair.Second >= pair.First));
    }

    [Fact]
    public void AngleToShockStrokeDataset_UsesTheSameTravelSteps_AsShockStrokeMapping()
    {
        var characteristics = CreateCharacteristics();
        var mapping = new JointNameMapping();

        var angleDataset = characteristics.AngleToShockStrokeDataset(mapping.RearWheel, mapping.BottomBracket, mapping.ShockEye1);
        var strokeDataset = characteristics.ShockStrokeToWheelTravelDataset();

        Assert.Equal(strokeDataset.X.Count, angleDataset.X.Count);
        Assert.Equal(0, angleDataset.Y[0], 6);
        Assert.True(angleDataset.Y[^1] > angleDataset.Y[0]);
        Assert.NotEqual(angleDataset.X[0], angleDataset.X[^1]);
    }

    [Fact]
    public void LeverageRatioData_UsesShockStrokeToWheelTravelSegments()
    {
        var characteristics = CreateCharacteristics();

        var strokeDataset = characteristics.ShockStrokeToWheelTravelDataset();
        var leverageRatioData = characteristics.LeverageRatioData;

        Assert.Equal(strokeDataset.X.Count - 1, leverageRatioData.X.Count);
        Assert.Equal(strokeDataset.Y.Count - 1, leverageRatioData.Y.Count);
        for (var index = 1; index < strokeDataset.Count; index++)
        {
            var expectedWheelTravel = (strokeDataset.Y[index - 1] + strokeDataset.Y[index]) / 2.0;
            var expectedRatio = (strokeDataset.Y[index] - strokeDataset.Y[index - 1]) /
                (strokeDataset.X[index] - strokeDataset.X[index - 1]);
            Assert.Equal(expectedWheelTravel, leverageRatioData.X[index - 1], 6);
            Assert.Equal(expectedRatio, leverageRatioData.Y[index - 1], 6);
        }
    }

    private static BikeCharacteristics CreateCharacteristics()
    {
        var solution = new KinematicSolver(TestLinkages.FullSuspensionLinkage(includeHeadTubeJoints: true))
            .SolveSuspensionMotion();

        return new BikeCharacteristics(solution);
    }
}
