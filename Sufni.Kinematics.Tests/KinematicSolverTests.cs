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

        var firstCoordinates = firstSolution.ToCoordinateDictionary();
        var secondCoordinates = secondSolution.ToCoordinateDictionary();
        AssertCoordinateListsEqual(firstCoordinates[mapping.RearWheel], secondCoordinates[mapping.RearWheel]);
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
        var coordinates = solution.ToCoordinateDictionary();

        Assert.Equal(1.0, coordinates["moving"].X[1], 6);
        Assert.Equal(0.0, coordinates["moving"].Y[1], 6);
    }

    [Fact]
    public void ToCoordinateDictionary_ReturnsCopies()
    {
        var linkage = TestLinkages.FullSuspensionLinkageSpec();
        var solution = new KinematicSolver(linkage, steps: 2, iterations: 1).SolveSuspensionMotion();
        var mapping = new JointNameMapping();

        var firstCoordinates = solution.ToCoordinateDictionary();
        firstCoordinates[mapping.RearWheel].X[0] = 1000;
        var secondCoordinates = solution.ToCoordinateDictionary();

        Assert.NotEqual(1000, secondCoordinates[mapping.RearWheel].X[0]);
        Assert.True(solution.TryGetPath(mapping.RearWheel, out var path));
        Assert.NotEqual(1000, path.X[0]);
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
