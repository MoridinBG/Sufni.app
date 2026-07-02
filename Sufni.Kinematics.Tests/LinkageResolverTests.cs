using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class LinkageResolverTests
{
    [Fact]
    public void Resolve_ResolvesValidSpec()
    {
        var spec = ValidSpec();

        var resolved = LinkageResolver.Resolve(spec);

        Assert.Same(spec, resolved.Spec);
        Assert.Equal(3, resolved.Joints.Count);
        var link = Assert.Single(resolved.Links);
        Assert.Equal("A", link.AName);
        Assert.Equal("B", link.BName);
        Assert.Same(resolved.Joints[0], link.A);
        Assert.Same(resolved.Joints[1], link.B);
        Assert.Same(resolved.Joints[1], resolved.Shock.A);
        Assert.Same(resolved.Joints[2], resolved.Shock.B);
        Assert.Equal(5, link.Length, 6);
        Assert.Equal(4, resolved.Shock.Length, 6);
    }

    [Fact]
    public void Resolve_CopiesCoordinatesIntoFreshMutableRuntimeState()
    {
        var spec = ValidSpec();

        var first = LinkageResolver.Resolve(spec);
        var second = LinkageResolver.Resolve(spec);
        first.Joints[0].X = 100;

        Assert.NotSame(first.Joints[0], second.Joints[0]);
        Assert.Equal(0, spec.Joints[0].X);
        Assert.Equal(0, second.Joints[0].X);
    }

    [Fact]
    public void Resolve_TreatsNullTypedJointAsNonFixed()
    {
        var spec = new LinkageSpec(
            [
                new JointSpec("A", null, 0, 0),
                new JointSpec("B", JointType.Fixed, 3, 4),
                new JointSpec("C", JointType.BottomBracket, 3, 8)
            ],
            [new LinkSpec("A", "B")],
            new LinkSpec("B", "C"),
            10);

        var resolved = LinkageResolver.Resolve(spec);

        Assert.False(resolved.Joints[0].IsFixed);
        Assert.True(resolved.Joints[1].IsFixed);
        Assert.True(resolved.Joints[2].IsFixed);
    }

    [Fact]
    public void Resolve_ThrowsDuplicateJointName()
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
    public void Resolve_ThrowsMissingLinkJoint()
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
    public void Resolve_ThrowsMissingShockJoint()
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
    public void Resolve_ThrowsDuplicateLink_ForReversedEndpointPair()
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
    public void Resolve_ThrowsDegenerateLink()
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
    public void Resolve_ThrowsDegenerateShock()
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
    public void Resolve_ThrowsMissingRequiredJointName()
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
        var exception = Assert.Throws<LinkageValidationException>(() => LinkageResolver.Resolve(spec));
        Assert.Equal(expectedCode, exception.Code);
    }

    private static LinkageSpec ValidSpec() => new(
        [
            new JointSpec("A", JointType.Fixed, 0, 0),
            new JointSpec("B", JointType.Floating, 3, 4),
            new JointSpec("C", JointType.Floating, 3, 8)
        ],
        [new LinkSpec("A", "B")],
        new LinkSpec("B", "C"),
        10);
}
