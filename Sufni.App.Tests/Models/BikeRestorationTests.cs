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
    public void FromSnapshot_PreservesShockStrokeWheelStateRotationAndLinkageStructure()
    {
        var source = new Bike(Guid.NewGuid(), "restored bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            PixelsToMillimeters = 1,
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = Math.Round(EtrtoRimSize.Inch29.CalculateTotalDiameterMm(2.4), 1),
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = Math.Round(EtrtoRimSize.Inch275.CalculateTotalDiameterMm(2.5), 1),
            ImageRotationDegrees = 12.5,
            Image = TestImages.SmallPng(),
            Linkage = TestSnapshots.FullSuspensionLinkage(),
            Updated = 7,
        };
        source.ShockStroke = 0.5;

        var snapshot = BikeSnapshot.From(source);
        var restored = Bike.FromSnapshot(snapshot);

        Assert.Equal(snapshot.ShockStroke, restored.ShockStroke);
        Assert.Equal(snapshot.Chainstay, restored.Chainstay);
        Assert.Equal(snapshot.FrontWheelRimSize, restored.FrontWheelRimSize);
        Assert.Equal(snapshot.FrontWheelTireWidth, restored.FrontWheelTireWidth);
        Assert.Equal(snapshot.FrontWheelDiameterMm, restored.FrontWheelDiameterMm);
        Assert.Equal(snapshot.RearWheelRimSize, restored.RearWheelRimSize);
        Assert.Equal(snapshot.RearWheelTireWidth, restored.RearWheelTireWidth);
        Assert.Equal(snapshot.RearWheelDiameterMm, restored.RearWheelDiameterMm);
        Assert.Equal(snapshot.ImageRotationDegrees, restored.ImageRotationDegrees);
        Assert.Equal(snapshot.Updated, restored.Updated);
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