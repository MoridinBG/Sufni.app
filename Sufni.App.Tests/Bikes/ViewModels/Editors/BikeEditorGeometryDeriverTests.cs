using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

public class BikeEditorGeometryDeriverTests
{
    public enum IncompleteGeometry { UpperHeadTube, LowerHeadTube, FrontWheel, RearWheel, FrontDiameter, RearDiameter, Scale, CoincidentHeadTube, CoincidentContacts }

    [Theory]
    [InlineData(IncompleteGeometry.UpperHeadTube)]
    [InlineData(IncompleteGeometry.LowerHeadTube)]
    [InlineData(IncompleteGeometry.FrontWheel)]
    [InlineData(IncompleteGeometry.RearWheel)]
    [InlineData(IncompleteGeometry.FrontDiameter)]
    [InlineData(IncompleteGeometry.RearDiameter)]
    [InlineData(IncompleteGeometry.Scale)]
    [InlineData(IncompleteGeometry.CoincidentHeadTube)]
    [InlineData(IncompleteGeometry.CoincidentContacts)]
    public void CalculationRequiresADefinedHeadTubeAndGroundLine(IncompleteGeometry missing)
    {
        var points = TestSnapshots.FullSuspensionLinkageSpec(true).Joints;
        JointSpec Point(string name) => points.Single(j => j.Name == name);
        var complete = new HeadAngleGeometry(Point(JointNames.HeadTube1), Point(JointNames.HeadTube2),
            Point(JointNames.FrontWheel), Point(JointNames.RearWheel), 700, 700, 1);
        var geometry = missing switch
        {
            IncompleteGeometry.UpperHeadTube => complete with { HeadTube1 = null },
            IncompleteGeometry.LowerHeadTube => complete with { HeadTube2 = null },
            IncompleteGeometry.FrontWheel => complete with { FrontWheel = null },
            IncompleteGeometry.RearWheel => complete with { RearWheel = null },
            IncompleteGeometry.FrontDiameter => complete with { FrontDiameter = null },
            IncompleteGeometry.RearDiameter => complete with { RearDiameter = null },
            IncompleteGeometry.Scale => complete with { Scale = null },
            IncompleteGeometry.CoincidentHeadTube => complete with { HeadTube1 = complete.HeadTube2 },
            IncompleteGeometry.CoincidentContacts => complete with { FrontWheel = complete.RearWheel },
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        };
        Assert.Null(BikeEditorGeometryDeriver.PhotoHeadAngle(RearSuspensionKind.Linkage, true, geometry));
    }

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
