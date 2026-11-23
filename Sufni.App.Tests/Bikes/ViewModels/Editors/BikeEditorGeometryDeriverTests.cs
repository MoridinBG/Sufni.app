using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

public class BikeEditorGeometryDeriverTests
{
    [Theory]
    [InlineData(RearSuspensionKind.Linkage, true, 71.6)]
    [InlineData(RearSuspensionKind.Linkage, false, null)]
    [InlineData(RearSuspensionKind.LeverageRatio, true, null)]
    public void PhotoAngleUsesScaledScreenCoordinatesOnlyForALinkagePhoto(
        RearSuspensionKind mode, bool hasImage, double? expected)
    {
        var spec = TestSnapshots.FullSuspensionLinkageSpec(true);
        JointSpec PixelJoint(string name)
        {
            var joint = spec.Joints.Single(joint => joint.Name == name);
            return joint with { X = joint.X / 2, Y = 50 - joint.Y / 2 };
        }

        var geometry = new HeadAngleGeometry(PixelJoint(JointNames.HeadTube1), PixelJoint(JointNames.HeadTube2),
            PixelJoint(JointNames.FrontWheel), PixelJoint(JointNames.RearWheel), 702, 700, 2);
        Assert.Equal(expected, BikeEditorGeometryDeriver.PhotoHeadAngle(mode, hasImage, geometry));
    }
}
