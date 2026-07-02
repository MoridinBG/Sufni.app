using Sufni.Kinematics;
using Sufni.App.Bikes.Services;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Bikes.Services;

public class KinematicSolutionCacheTests
{
    [Fact]
    public void GetOrSolve_ReturnsSharedSolution_ForStructurallyEqualLinkageAndSolverSettings()
    {
        var calls = 0;
        var cache = new KinematicSolutionCache((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return CreateSolution(calls);
        });
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var structurallyEqualLinkage = Clone(linkage);

        var first = cache.GetOrSolve(linkage);
        var second = cache.GetOrSolve(structurallyEqualLinkage);

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrSolve_Misses_WhenProcessingInputsOrSolverSettingsDiffer()
    {
        var calls = 0;
        var cache = new KinematicSolutionCache((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return CreateSolution(calls);
        });
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var shockStrokeChanged = linkage.WithShockStroke(linkage.ShockStroke + 0.1);
        var jointCoordinateChanged = WithFirstJointX(linkage, linkage.Joints[0].X + 0.1);

        var baseline = cache.GetOrSolve(linkage);
        var shockStroke = cache.GetOrSolve(shockStrokeChanged);
        var jointCoordinate = cache.GetOrSolve(jointCoordinateChanged);
        var steps = cache.GetOrSolve(linkage, steps: 201);
        var iterations = cache.GetOrSolve(linkage, iterations: 1001);

        Assert.NotSame(baseline, shockStroke);
        Assert.NotSame(baseline, jointCoordinate);
        Assert.NotSame(baseline, steps);
        Assert.NotSame(baseline, iterations);
        Assert.Equal(5, calls);
    }

    [Fact]
    public void GetOrSolve_Retries_WhenSolveFails()
    {
        var calls = 0;
        var cache = new KinematicSolutionCache((_, _, _) =>
        {
            var attempt = Interlocked.Increment(ref calls);
            if (attempt == 1)
            {
                throw new InvalidOperationException("solve failed");
            }

            return CreateSolution(attempt);
        });
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();

        Assert.Throws<InvalidOperationException>(() => cache.GetOrSolve(linkage));
        var solution = cache.GetOrSolve(linkage);

        Assert.NotNull(solution);
        Assert.Equal(2, calls);
    }

    private static LinkageSpec Clone(LinkageSpec linkage) =>
        new(linkage.Joints, linkage.Links, linkage.Shock, linkage.ShockStroke);

    private static LinkageSpec WithFirstJointX(LinkageSpec linkage, double x)
    {
        var joints = linkage.Joints
            .Select((joint, index) => index == 0 ? joint with { X = x } : joint)
            .ToArray();

        return new LinkageSpec(joints, linkage.Links, linkage.Shock, linkage.ShockStroke);
    }

    private static KinematicSolution CreateSolution(int value) =>
        new([
            new JointPath(
                "rear-wheel",
                [value],
                [value])
        ]);
}
