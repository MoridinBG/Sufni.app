using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class BikeCharacteristicsRearMappingTests
{
    [Fact]
    public void AngleToShockStrokeDataset_WhenSensorJointIsMissing_ReportsMissingJointPath()
    {
        var characteristics = CreateCharacteristics();

        var failure = Assert.Throws<KinematicCalculationException>(() =>
            characteristics.AngleToShockStrokeDataset("removed sensor joint", JointNames.BottomBracket, JointNames.ShockEye1));

        Assert.Equal(KinematicCalculationFailureReason.MissingJointPath, failure.Reason);
    }

    [Fact]
    public void ShockStrokeToWheelTravelDataset_MapsKnownShockAndWheelDistances()
    {
        var dataset = CreateKnownCharacteristics().ShockStrokeToWheelTravelDataset();

        Assert.Equal([0, 1, 3], dataset.X);
        Assert.Equal([0, 5, 10], dataset.Y);
    }

    [Fact]
    public void AngleToShockStrokeDataset_PairsKnownAnglesWithShockShortening()
    {
        var dataset = CreateKnownCharacteristics().AngleToShockStrokeDataset(
            JointNames.ShockEye2, JointNames.ShockEye1, JointNames.BottomBracket);

        Assert.Equal(Math.PI / 2, dataset.X[0], 10);
        Assert.Equal(Math.PI / 4, dataset.X[1], 10);
        Assert.Equal(0, dataset.X[2], 10);
        Assert.Equal([0, 1, 3], dataset.Y);
    }

    [Fact]
    public void LeverageRatioData_UsesKnownWheelMidpointsAndRatios()
    {
        var dataset = CreateKnownCharacteristics().LeverageRatioData;

        Assert.Equal([2.5, 7.5], dataset.X);
        Assert.Equal([5, 2.5], dataset.Y);
    }

    [Fact]
    public void LeverageRatioData_PinsMidpointXConvention_AtCurveEndpoints()
    {
        var characteristics = CreateCharacteristics();

        var strokeDataset = characteristics.ShockStrokeToWheelTravelDataset();
        var leverageRatioData = characteristics.LeverageRatioData;

        var firstSegmentMidpoint = (strokeDataset.Y[0] + strokeDataset.Y[1]) / 2.0;
        var lastSegmentMidpoint = (strokeDataset.Y[^2] + strokeDataset.Y[^1]) / 2.0;
        Assert.Equal(firstSegmentMidpoint, leverageRatioData.X[0], 6);
        Assert.Equal(lastSegmentMidpoint, leverageRatioData.X[^1], 6);
        // Deliberate convention: a finite-difference ratio is plotted at the
        // segment midpoint, so the curve stops half a segment short of zero
        // and of max wheel travel.
        Assert.True(leverageRatioData.X[0] > strokeDataset.Y[0]);
        Assert.True(leverageRatioData.X[^1] < strokeDataset.Y[^1]);
    }

    private static BikeCharacteristics CreateKnownCharacteristics() => new(new KinematicSolution(
    [
        new JointPath(JointNames.RearWheel, [0, 3, 6], [0, 4, 8]),
        new JointPath(JointNames.ShockEye1, [10, 9, 7], [0, 0, 0]),
        new JointPath(JointNames.ShockEye2, [0, 0, 0], [0, 0, 0]),
        new JointPath(JointNames.BottomBracket, [0, 1, 1], [1, 1, 0]),
    ]));

    private static BikeCharacteristics CreateCharacteristics()
    {
        var solution = KinematicSolver.Solve(TestLinkages.FullSuspensionLinkageSpec(includeHeadTubeJoints: true));

        return new BikeCharacteristics(solution);
    }
}
