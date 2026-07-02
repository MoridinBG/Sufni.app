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
    public void FromSnapshot_PreservesShockStrokeChainstayRotationAndLinkageStructure()
    {
        var source = new Bike(Guid.NewGuid(), "restored bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            PixelsToMillimeters = 1,
            ImageRotationDegrees = 12.5,
            ImageBytes = TestImages.SmallPngBytes(),
            FrontCompressionDampingCutoffMmPerSecond = 115,
            FrontReboundDampingCutoffMmPerSecond = 125,
            RearCompressionDampingCutoffMmPerSecond = 235,
            RearReboundDampingCutoffMmPerSecond = 245,
            Updated = 7,
        };
        var linkage = TestSnapshots.FullSuspensionLinkage();
        source.RearSuspension = new RearSuspensionSpec.Linkage(linkage.ToSpec());
        source.ShockStroke = 0.5;

        var snapshot = BikeSnapshot.From(source);
        var restored = Bike.FromSnapshot(snapshot);

        Assert.Equal(snapshot.ShockStroke, restored.ShockStroke);
        Assert.Equal(snapshot.FrontCompressionDampingCutoffMmPerSecond, restored.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(snapshot.FrontReboundDampingCutoffMmPerSecond, restored.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(snapshot.RearCompressionDampingCutoffMmPerSecond, restored.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(snapshot.RearReboundDampingCutoffMmPerSecond, restored.RearReboundDampingCutoffMmPerSecond);
        Assert.Equal(snapshot.Chainstay, restored.Chainstay);
        Assert.Equal(snapshot.ImageRotationDegrees, restored.ImageRotationDegrees);
        var restoredLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(restored.RearSuspension);
        Assert.Equal(snapshot.ShockStroke, restoredLinkage.Spec.ShockStroke);
        Assert.Equal(DescribeJoints(snapshot.Linkage!.Joints), DescribeJoints(restoredLinkage.Spec.Joints));
        Assert.Equal(DescribeLinks(snapshot.Linkage.Links.Append(snapshot.Linkage.Shock)), DescribeLinks(restoredLinkage.Spec.Links.Append(restoredLinkage.Spec.Shock)));
    }

    private static IReadOnlyList<(string Name, JointType? Type, double X, double Y)> DescribeJoints(IEnumerable<JointSpec> joints) =>
        joints
            .OrderBy(joint => joint.Name)
            .Select(joint => (joint.Name ?? string.Empty, joint.Type, Math.Round(joint.X, 3), Math.Round(joint.Y, 3)))
            .ToList();

    private static IReadOnlyList<string> DescribeLinks(IEnumerable<LinkSpec> links) =>
        links
            .Select(DescribeLink)
            .OrderBy(link => link)
            .ToList();

    private static string DescribeLink(LinkSpec link) =>
        string.CompareOrdinal(link.A, link.B) <= 0
            ? $"{link.A}->{link.B}"
            : $"{link.B}->{link.A}";
}
