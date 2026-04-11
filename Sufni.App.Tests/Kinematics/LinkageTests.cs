using Sufni.Kinematics;

namespace Sufni.App.Tests.Kinematics;

public class LinkageTests
{
    [Fact]
    public void LinkConstructor_ComputesLengthFromJointCoordinates()
    {
        var a = new Joint("A", JointType.Fixed, 0, 0);
        var b = new Joint("B", JointType.Floating, 3, 4);

        var link = new Link(a, b);

        Assert.Equal(5, link.Length, 3);
    }

    [Fact]
    public void CreateResolved_ComputesLinkAndShockLengths()
    {
        var bottomBracket = new Joint("Bottom bracket", JointType.BottomBracket, 0, 0);
        var rearWheel = new Joint("Rear wheel", JointType.RearWheel, 4, 0);
        var shockEye1 = new Joint("Shock eye 1", JointType.Floating, 4, 3);
        var shockEye2 = new Joint("Shock eye 2", JointType.Fixed, 0, 3);

        var linkage = Linkage.CreateResolved(
            [bottomBracket, rearWheel, shockEye1, shockEye2],
            [new Link(bottomBracket, rearWheel), new Link(rearWheel, shockEye1)],
            new Link(shockEye1, shockEye2),
            0.5);

        Assert.All(linkage.Links, link => Assert.True(link.Length > 0));
        Assert.True(linkage.Shock.Length > 0);
    }
}