using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class KinematicSolverTests
{
    [Fact]
    public void Solve_DoesNotMutateInputSpec()
    {
        var linkage = TestLinkages.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var original = LinkageSpec.FromJson(linkage.ToJson());

        _ = KinematicSolver.Solve(linkage);

        Assert.Equal(original, linkage);
    }

    [Fact]
    public void Solve_IsDeterministic_ForSameSpec()
    {
        var linkage = TestLinkages.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);

        var firstSolution = KinematicSolver.Solve(linkage);
        var secondSolution = KinematicSolver.Solve(linkage);
        var firstCharacteristics = new BikeCharacteristics(firstSolution);
        var secondCharacteristics = new BikeCharacteristics(secondSolution);

        Assert.True(firstSolution.TryGetPath(JointNames.RearWheel, out var firstPath));
        Assert.True(secondSolution.TryGetPath(JointNames.RearWheel, out var secondPath));
        Assert.Equal(firstPath.X, secondPath.X);
        Assert.Equal(firstPath.Y, secondPath.Y);
        AssertCoordinateListsEqual(firstCharacteristics.LeverageRatioData, secondCharacteristics.LeverageRatioData);
    }

    [Fact]
    public void Solve_AppliesFullCorrection_WhenOnlyOneEndpointCanMove()
    {
        var linkage = new LinkageSpec(
            joints:
            [
                new JointSpec("fixed", JointType.Fixed, 0, 0),
                new JointSpec("moving", JointType.Floating, 2, 0)
            ],
            links: [],
            shock: new LinkSpec("fixed", "moving"),
            shockStroke: 1);

        var solution = KinematicSolver.Solve(linkage, steps: 2, iterations: 1);
        Assert.True(solution.TryGetPath("moving", out var path));

        Assert.Equal(1.0, path.X[1], 6);
        Assert.Equal(0.0, path.Y[1], 6);
    }

    [Fact]
    public void CoordinateList_KeepsInterpolationStableAfterInputChanges()
    {
        double[] shock = [0, 10, 20];
        double[] wheel = [0, 30, 50];
        var curve = new CoordinateList(shock, wheel);

        shock[1] = 100;
        wheel[1] = 1000;

        Assert.Equal(15, TravelInterpolation.WheelTravelAt(curve, 5));
    }

    private static void AssertCoordinateListsEqual(CoordinateList expected, CoordinateList actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected.X[i], actual.X[i], 6);
            Assert.Equal(expected.Y[i], actual.Y[i], 6);
        }
    }
}
