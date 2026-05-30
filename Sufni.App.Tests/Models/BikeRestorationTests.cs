using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Models;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Models;

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
            Linkage = TestSnapshots.FullSuspensionLinkage(),
            FrontCompressionDampingCutoffMmPerSecond = 115,
            FrontReboundDampingCutoffMmPerSecond = 125,
            RearCompressionDampingCutoffMmPerSecond = 235,
            RearReboundDampingCutoffMmPerSecond = 245,
            Updated = 7,
        };
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
        Assert.NotNull(restored.Linkage);
        Assert.Equal(snapshot.ShockStroke, restored.Linkage!.ShockStroke);
        Assert.Equal(DescribeJoints(snapshot.Linkage!.Joints), DescribeJoints(restored.Linkage.Joints));
        Assert.Equal(DescribeLinks(snapshot.Linkage.Links.Append(snapshot.Linkage.Shock)), DescribeLinks(restored.Linkage.Links.Append(restored.Linkage.Shock)));
    }

    private static IReadOnlyList<(string Name, JointType? Type, double X, double Y)> DescribeJoints(IEnumerable<Joint> joints) =>
        joints
            .OrderBy(joint => joint.Name)
            .Select(joint => (joint.Name ?? string.Empty, joint.Type, Math.Round(joint.X, 3), Math.Round(joint.Y, 3)))
            .ToList();

    private static IReadOnlyList<string> DescribeLinks(IEnumerable<Link> links) =>
        links
            .Select(DescribeLink)
            .OrderBy(link => link)
            .ToList();

    private static string DescribeLink(Link link)
    {
        var a = Assert.IsType<Joint>(link.A);
        var b = Assert.IsType<Joint>(link.B);
        return string.CompareOrdinal(a.Name, b.Name) <= 0
            ? $"{a.Name}->{b.Name}"
            : $"{b.Name}->{a.Name}";
    }
}
