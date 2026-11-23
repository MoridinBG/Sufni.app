using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Kinematics;

using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Models;

public class BikeRestorationTests
{
    [Fact]
    public void Chainstay_RecomputesFromCurrentLinkage_WhenNoExplicitValueExists()
    {
        var source = new Bike(Guid.NewGuid(), "linkage bike")
        {
            RearSuspension = new RearSuspensionSpec.Linkage(LinkageWithChainstay(4)),
        };

        Assert.Equal(4, source.Chainstay);

        source.RearSuspension = new RearSuspensionSpec.Linkage(LinkageWithChainstay(10));

        Assert.Equal(10, source.Chainstay);
    }

    [Fact]
    public void Chainstay_ReturnsNull_WhenRearSuspensionIsChangedToHardtail()
    {
        var source = new Bike(Guid.NewGuid(), "linkage bike")
        {
            RearSuspension = new RearSuspensionSpec.Linkage(LinkageWithChainstay(4)),
        };

        source.RearSuspension = new RearSuspensionSpec.Hardtail();

        Assert.Null(source.Chainstay);
    }

    [Theory]
    [InlineData(null, 702d, 700d, 71.6)]
    [InlineData(JointNames.HeadTube1, 702d, 700d, 65)]
    [InlineData(JointNames.HeadTube2, 702d, 700d, 65)]
    [InlineData(JointNames.FrontWheel, 702d, 700d, 65)]
    [InlineData(JointNames.RearWheel, 702d, 700d, 65)]
    [InlineData(null, null, 700d, 65)]
    [InlineData(null, 702d, null, 65)]
    public void SnapshotUsesPhotoAngleWhenAllGeometryIsAvailable(
        string? omittedJoint, double? frontDiameter, double? rearDiameter, double expected)
    {
        var spec = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var bike = new Bike(Guid.NewGuid(), "bike")
        {
            HeadAngle = 65,
            RearSuspension = new RearSuspensionSpec.Linkage(new LinkageSpec(
                spec.Joints.Where(joint => joint.Name != omittedJoint).ToArray(),
                spec.Links, spec.Shock, spec.ShockStroke)),
            FrontWheelDiameterMm = frontDiameter,
            RearWheelDiameterMm = rearDiameter,
        };

        Assert.Equal(expected, BikeSnapshot.From(bike).HeadAngle);
    }

    private static LinkageSpec LinkageWithChainstay(double chainstay)
    {

        return new LinkageSpec(
            [
                new JointSpec(JointNames.BottomBracket, JointType.BottomBracket, 0, 0),
                new JointSpec(JointNames.RearWheel, JointType.RearWheel, chainstay, 0),
                new JointSpec(JointNames.ShockEye1, JointType.Floating, chainstay, 3),
                new JointSpec(JointNames.ShockEye2, JointType.Fixed, 0, 3)
            ],
            [
                new LinkSpec(JointNames.BottomBracket, JointNames.RearWheel),
                new LinkSpec(JointNames.RearWheel, JointNames.ShockEye1)
            ],
            new LinkSpec(JointNames.ShockEye1, JointNames.ShockEye2),
            10);
    }
}
