using Sufni.App.Tests.Infrastructure;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Kinematics;

public class KinematicSolverTests
{
    [Fact]
    public void SolveSuspensionMotion_MatchesResolvedInput_WhenCallerLinkageHasUnresolvedLengths()
    {
        var resolvedLinkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var unresolvedLinkage = Linkage.FromJson(resolvedLinkage.ToJson(), resolve: false);

        var resolvedSolution = new KinematicSolver(resolvedLinkage).SolveSuspensionMotion();
        var unresolvedSolution = new KinematicSolver(unresolvedLinkage).SolveSuspensionMotion();
        var mapping = new JointNameMapping();
        var resolvedCharacteristics = new BikeCharacteristics(resolvedSolution);
        var unresolvedCharacteristics = new BikeCharacteristics(unresolvedSolution);

        AssertCoordinateListsEqual(resolvedSolution[mapping.RearWheel], unresolvedSolution[mapping.RearWheel]);
        AssertCoordinateListsEqual(resolvedCharacteristics.LeverageRatioData, unresolvedCharacteristics.LeverageRatioData);
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