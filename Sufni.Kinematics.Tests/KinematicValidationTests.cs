using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class KinematicValidationTests
{
    [Fact]
    public void Solve_MovesUntypedJointAndKeepsBottomBracketFixed()
    {
        var spec = new LinkageSpec(
            [new JointSpec("fixed", JointType.BottomBracket, 0, 0), new JointSpec("moving", null, 2, 0)],
            [], new LinkSpec("fixed", "moving"), 1);

        var solution = KinematicSolver.Solve(spec, steps: 2, iterations: 1);

        Assert.True(solution.TryGetPath("fixed", out var fixedPath));
        Assert.True(solution.TryGetPath("moving", out var movingPath));
        Assert.Equal([0.0, 0.0], fixedPath.X);
        Assert.Equal([2.0, 1.0], movingPath.X);
    }

    [Fact]
    public void Solve_ThrowsDuplicateJointName()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", JointType.Fixed, 0, 0),
                new JointSpec("A", JointType.Floating, 3, 4),
                new JointSpec("C", JointType.Floating, 3, 8)
            ],
            [new LinkSpec("A", "C")],
            new LinkSpec("A", "C"),
            10);

        AssertValidationCode(LinkageValidationErrorCode.DuplicateJointName, spec);
    }

    [Fact]
    public void Solve_ThrowsMissingLinkJoint()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", JointType.Fixed, 0, 0),
                new JointSpec("B", JointType.Floating, 3, 4),
                new JointSpec("C", JointType.Floating, 3, 8)
            ],
            [new LinkSpec("A", "Missing")],
            new LinkSpec("B", "C"),
            10);

        AssertValidationCode(LinkageValidationErrorCode.MissingLinkJoint, spec);
    }

    [Fact]
    public void Solve_ThrowsMissingShockJoint()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", JointType.Fixed, 0, 0),
                new JointSpec("B", JointType.Floating, 3, 4)
            ],
            [new LinkSpec("A", "B")],
            new LinkSpec("A", "Missing"),
            10);

        AssertValidationCode(LinkageValidationErrorCode.MissingShockJoint, spec);
    }

    [Fact]
    public void Solve_ThrowsDuplicateLink_ForReversedEndpointPair()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", JointType.Fixed, 0, 0),
                new JointSpec("B", JointType.Floating, 3, 4),
                new JointSpec("C", JointType.Floating, 3, 8)
            ],
            [
                new LinkSpec("A", "B"),
                new LinkSpec("B", "A")
            ],
            new LinkSpec("B", "C"),
            10);

        AssertValidationCode(LinkageValidationErrorCode.DuplicateLink, spec);
    }

    [Fact]
    public void Solve_ThrowsDegenerateLink()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", JointType.Fixed, 0, 0),
                new JointSpec("B", JointType.Floating, 0, 0),
                new JointSpec("C", JointType.Floating, 0, 4)
            ],
            [new LinkSpec("A", "B")],
            new LinkSpec("B", "C"),
            10);

        AssertValidationCode(LinkageValidationErrorCode.DegenerateLink, spec);
    }

    [Fact]
    public void Solve_ThrowsDegenerateShock()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", JointType.Fixed, 0, 0),
                new JointSpec("B", JointType.Floating, 3, 4),
                new JointSpec("C", JointType.Floating, 3, 4)
            ],
            [new LinkSpec("A", "B")],
            new LinkSpec("B", "C"),
            10);

        AssertValidationCode(LinkageValidationErrorCode.DegenerateShock, spec);
    }

    [Fact]
    public void Solve_ThrowsMissingRequiredJointName()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", JointType.Fixed, 0, 0),
                new JointSpec(null!, JointType.Floating, 3, 4),
                new JointSpec("C", JointType.Floating, 3, 8)
            ],
            [new LinkSpec("A", "C")],
            new LinkSpec("A", "C"),
            10);

        AssertValidationCode(LinkageValidationErrorCode.MissingRequiredJointName, spec);
    }

    private static void AssertValidationCode(LinkageValidationErrorCode expectedCode, LinkageSpec spec)
    {
        var exception = Assert.Throws<LinkageValidationException>(() => KinematicSolver.Solve(spec));
        Assert.Equal(expectedCode, exception.Code);
    }

}
