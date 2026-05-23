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

}
