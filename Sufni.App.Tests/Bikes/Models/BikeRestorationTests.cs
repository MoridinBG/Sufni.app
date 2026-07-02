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
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        source.RearSuspension = new RearSuspensionSpec.Linkage(linkage);
        source.ShockStroke = linkage.ShockStroke;

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

    [Fact]
    public void FromSnapshot_CopiesImageBytes()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            ImageBytes = [1, 2, 3],
        };

        var restored = Bike.FromSnapshot(snapshot);
        restored.ImageBytes[0] = 9;

        Assert.Equal([1, 2, 3], snapshot.ImageBytes);
    }

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
            Chainstay = 440,
            RearSuspension = new RearSuspensionSpec.Linkage(LinkageWithChainstay(4)),
        };

        var updated = source.WithRearSuspension(new RearSuspensionSpec.Hardtail());

        Assert.Null(updated.Chainstay);
    }

    [Fact]
    public void WithShockStroke_OnLinkageBike_RebuildsLinkageSpecWithoutMutatingOriginal()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var source = new Bike(Guid.NewGuid(), "linkage bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            Updated = 7,
        };

        var updated = source.WithShockStroke(0.75);

        Assert.NotSame(source, updated);
        Assert.Equal(linkage.ShockStroke, source.ShockStroke);
        Assert.Equal(linkage.ShockStroke, Assert.IsType<RearSuspensionSpec.Linkage>(source.RearSuspension).Spec.ShockStroke);
        Assert.Equal(0.75, updated.ShockStroke);
        Assert.Equal(0.75, Assert.IsType<RearSuspensionSpec.Linkage>(updated.RearSuspension).Spec.ShockStroke);
        Assert.Equal(source.Id, updated.Id);
        Assert.Equal(source.Name, updated.Name);
    }

    [Fact]
    public void WithRearSuspension_ReturnsNewBikeWithoutMutatingOriginal()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var source = new Bike(Guid.NewGuid(), "hardtail bike")
        {
            HeadAngle = 64,
            ForkStroke = 170,
            RearSuspension = new RearSuspensionSpec.Hardtail(),
            Updated = 7,
        };

        var updated = source.WithRearSuspension(new RearSuspensionSpec.LeverageRatio(leverageRatio));

        Assert.NotSame(source, updated);
        Assert.IsType<RearSuspensionSpec.Hardtail>(source.RearSuspension);
        var updatedLeverageRatio = Assert.IsType<RearSuspensionSpec.LeverageRatio>(updated.RearSuspension);
        Assert.Equal(leverageRatio, updatedLeverageRatio.Spec);
        Assert.Equal(source.Id, updated.Id);
        Assert.Equal(source.Name, updated.Name);
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

    private static LinkageSpec LinkageWithChainstay(double chainstay)
    {
        var mapping = new JointNameMapping();

        return new LinkageSpec(
            [
                new JointSpec(mapping.BottomBracket, JointType.BottomBracket, 0, 0),
                new JointSpec(mapping.RearWheel, JointType.RearWheel, chainstay, 0),
                new JointSpec(mapping.ShockEye1, JointType.Floating, chainstay, 3),
                new JointSpec(mapping.ShockEye2, JointType.Fixed, 0, 3)
            ],
            [
                new LinkSpec(mapping.BottomBracket, mapping.RearWheel),
                new LinkSpec(mapping.RearWheel, mapping.ShockEye1)
            ],
            new LinkSpec(mapping.ShockEye1, mapping.ShockEye2),
            10);
    }
}
