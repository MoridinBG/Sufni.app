using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class KinematicSolverTests
{
    [Fact]
    public void SolveSuspensionMotion_DoesNotMutateInputSpec()
    {
        var linkage = TestLinkages.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var original = LinkageSpec.FromJson(linkage.ToJson());

        _ = new KinematicSolver(linkage).SolveSuspensionMotion();

        Assert.Equal(original, linkage);
    }

    [Fact]
    public void SolveSuspensionMotion_IsDeterministic_ForSameSpec()
    {
        var linkage = TestLinkages.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);

        var firstSolution = new KinematicSolver(linkage).SolveSuspensionMotion();
        var secondSolution = new KinematicSolver(linkage).SolveSuspensionMotion();
        var mapping = new JointNameMapping();
        var firstCharacteristics = new BikeCharacteristics(firstSolution);
        var secondCharacteristics = new BikeCharacteristics(secondSolution);

        Assert.True(firstSolution.TryGetPath(mapping.RearWheel, out var firstPath));
        Assert.True(secondSolution.TryGetPath(mapping.RearWheel, out var secondPath));
        Assert.Equal(firstPath.X, secondPath.X);
        Assert.Equal(firstPath.Y, secondPath.Y);
        AssertCoordinateListsEqual(firstCharacteristics.LeverageRatioData, secondCharacteristics.LeverageRatioData);
    }

    [Fact]
    public void SolveSuspensionMotion_AppliesFullCorrection_WhenOnlyOneEndpointCanMove()
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

        var solution = new KinematicSolver(linkage, steps: 2, iterations: 1).SolveSuspensionMotion();
        Assert.True(solution.TryGetPath("moving", out var path));

        Assert.Equal(1.0, path.X[1], 6);
        Assert.Equal(0.0, path.Y[1], 6);
    }

    [Fact]
    public void BikeCharacteristics_ReturnsCoordinateCopies()
    {
        var linkage = TestLinkages.FullSuspensionLinkageSpec();
        var solution = new KinematicSolver(linkage).SolveSuspensionMotion();
        var characteristics = new BikeCharacteristics(solution);

        var first = characteristics.LeverageRatioData;
        first.X[0] = 1000;
        var second = characteristics.LeverageRatioData;

        Assert.NotEqual(1000, second.X[0]);
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
