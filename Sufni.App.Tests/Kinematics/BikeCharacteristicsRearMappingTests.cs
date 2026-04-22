using Sufni.App.Tests.Infrastructure;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Kinematics;

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

    private static BikeCharacteristics CreateCharacteristics()
    {
        var solution = new KinematicSolver(TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true))
            .SolveSuspensionMotion();

        return new BikeCharacteristics(solution);
    }
}