using Avalonia;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

public class BikeEditorLinkageGeometryTests
{
    private static BikeEditorStateController Open(LinkageSpec spec)
    {
        var draft = BikeDraft.From(TestSnapshots.Bike()) with
        {
            RearSuspension = new RearSuspensionSpec.Linkage(spec), ShockStroke = spec.ShockStroke,
            FrontWheel = new(702, null, null), RearWheel = new(700, null, null),
            PixelsToMillimeters = 2, ImageBytes = [1], HeadAngle = 71.6
        };
        var controller = new BikeEditorStateController(draft, true);
        controller.Actions.Prepared(draft, new BikeEditorImage(draft.ImageBytesMemory, new Size(200, 50)), BikeDraftLoadKind.Open);
        return controller;
    }

    public enum ChangedField { Angle, Fork, Stroke, Mode, FrontWidth, RearDiameter, Rim, Image, Rotation, JointType, Link, Shock }

    [Theory]
    [InlineData(ChangedField.Angle)]
    [InlineData(ChangedField.Fork)]
    [InlineData(ChangedField.Stroke)]
    [InlineData(ChangedField.Mode)]
    [InlineData(ChangedField.FrontWidth)]
    [InlineData(ChangedField.RearDiameter)]
    [InlineData(ChangedField.Rim)]
    [InlineData(ChangedField.Image)]
    [InlineData(ChangedField.Rotation)]
    [InlineData(ChangedField.JointType)]
    [InlineData(ChangedField.Link)]
    [InlineData(ChangedField.Shock)]
    public void ComparisonIncludesEachAuthoredLinkageField(ChangedField field)
    {
        var spec = TestSnapshots.FullSuspensionLinkageSpec(true);
        using var editor = Open(spec);
        var state = editor.Current;
        var baseline = state.Baseline;
        var changed = field switch
        {
            ChangedField.Angle => baseline with { HeadAngle = 67 },
            ChangedField.Fork => baseline with { ForkStroke = baseline.ForkStroke + 1 },
            ChangedField.Stroke => baseline with { ShockStroke = 10 },
            ChangedField.Mode => baseline with { RearSuspension = new RearSuspensionSpec.Hardtail() },
            ChangedField.FrontWidth => baseline with { FrontWheel = baseline.FrontWheel! with { TireWidth = 2.5 } },
            ChangedField.RearDiameter => baseline with { RearWheel = baseline.RearWheel! with { DiameterMm = 720 } },
            ChangedField.Rim => baseline with { FrontWheel = baseline.FrontWheel! with { RimSize = EtrtoRimSize.Inch29 } },
            ChangedField.Image => baseline with { ImageBytes = [2] },
            ChangedField.Rotation => baseline with { ImageRotationDegrees = 0.5 },
            ChangedField.JointType => WithLinkage(new LinkageSpec(
                [.. spec.Joints.Select(j => j.Name == JointNames.ShockEye1 ? j with { Type = JointType.Fixed } : j)],
                spec.Links, spec.Shock, spec.ShockStroke)),
            ChangedField.Link => WithLinkage(new LinkageSpec(spec.Joints, [], spec.Shock, spec.ShockStroke)),
            ChangedField.Shock => WithLinkage(new LinkageSpec(spec.Joints, spec.Links, spec.Links[0], spec.ShockStroke)),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.False(state.Geometry.InputsDirty);
        Assert.True(BikeAuthoredDraftComparison.HasChanges(state.Inputs, changed, state.Geometry.EffectiveHeadAngle, state.Geometry.Spec));

        BikeDraft WithLinkage(LinkageSpec value) => baseline with { RearSuspension = new RearSuspensionSpec.Linkage(value) };
    }

    [Fact]
    public void WheelOverlaysAreCenteredOnTheirJointsAtTheCurrentScale()
    {
        using var editor = Open(TestSnapshots.FullSuspensionLinkageSpec(true));
        var geometry = editor.Current.Geometry;
        var joints = editor.Current.Inputs.Linkage.Joints;
        var scale = geometry.Scale!.Value;

        foreach (var (overlay, type, diameter) in new[] { (geometry.Front, JointType.FrontWheel, 702.0), (geometry.Rear, JointType.RearWheel, 700.0) })
        {
            var joint = joints.Single(j => j.Type == type);
            Assert.Equal(diameter / scale, overlay.Diameter);
            Assert.Equal(new Point(joint.X, joint.Y), new Point(overlay.Left + overlay.Diameter / 2, overlay.Top + overlay.Diameter / 2));
        }
    }

    [Fact]
    public void ContentBoundsIncludeTheImageTheWheelsAndEveryJoint()
    {
        using var editor = Open(TestSnapshots.FullSuspensionLinkageSpec(true));
        var geometry = editor.Current.Geometry;

        Assert.True(geometry.ContentBounds.Contains(geometry.ImageBounds));
        foreach (var wheel in new[] { geometry.Front, geometry.Rear })
            Assert.True(geometry.ContentBounds.Contains(new Rect(wheel.Left, wheel.Top, wheel.Diameter, wheel.Diameter)));
        Assert.All(editor.Current.Inputs.Linkage.Joints, joint => Assert.True(geometry.ContentBounds.Contains(new Point(joint.X, joint.Y))));
    }

    [Fact]
    public void LinkLengthsUseTheCurrentScale()
    {
        using var editor = Open(TestSnapshots.FullSuspensionLinkageSpec(true));
        var geometry = editor.Current.Geometry;
        var linkage = editor.Current.Inputs.Linkage;

        Assert.All(linkage.Links, link =>
        {
            var (a, b) = (linkage.Joint(link.A)!, linkage.Joint(link.B)!);
            Assert.Equal(geometry.Scale!.Value * Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)), geometry.LinkLengths[link.Id], 9);
        });
    }

    [Fact]
    public void ReorderedJointsAndLinksAndBaselineScaleDoNotDirtyTheDraft()
    {
        var spec = TestSnapshots.FullSuspensionLinkageSpec(true);
        using var editor = Open(spec);
        var baseline = editor.Current.Baseline with { PixelsToMillimeters = 1,
            RearSuspension = new RearSuspensionSpec.Linkage(new LinkageSpec([.. spec.Joints.Reverse()],
                [.. spec.Links.Reverse()], spec.Shock, spec.ShockStroke)) };
        editor.Actions.BaselineAccepted(baseline);
        Assert.False(editor.Current.Geometry.InputsDirty);

        var eye = editor.Current.Inputs.Linkage.Joints.Single(joint => joint.Name == JointNames.ShockEye1);
        editor.Actions.Move(eye.Id, eye.X + 0.0001, eye.Y);
        Assert.False(editor.Current.Geometry.InputsDirty);
        editor.Actions.Move(eye.Id, eye.X + 0.01, eye.Y);
        Assert.True(editor.Current.Geometry.InputsDirty);
    }

    [Theory]
    [InlineData(0.005, 0)]
    [InlineData(1, -1)]
    public void GroundAlignmentMovesTheImageAndJointsOnlyAboveThreshold(double tilt, double expectedRotation)
    {
        var spec = TestSnapshots.FullSuspensionLinkageSpec(true);
        using var editor = Open(spec);
        var front = editor.Current.Inputs.Linkage.Joints.Single(joint => joint.Type == JointType.FrontWheel);
        var rear = editor.Current.Inputs.Linkage.Joints.Single(joint => joint.Type == JointType.RearWheel);
        var y = rear.Y - 0.5 + Math.Tan(tilt * Math.PI / 180) * (front.X - rear.X);
        editor.Actions.Move(front.Id, front.X, y);
        editor.Actions.Align();
        Assert.Equal(expectedRotation, editor.Current.Inputs.Rotation, 8);
        var rotated = editor.Current.Inputs.Linkage.Joint(front.Id)!;
        Assert.Equal(expectedRotation == 0, rotated.X == front.X && rotated.Y == y);
    }

    [Fact]
    public void DeletingASelectedJointClearsSelectionInThePublishedState()
    {
        using var editor = Open(TestSnapshots.FullSuspensionLinkageSpec(true));
        editor.Actions.AddJoint();
        var id = editor.Current.Inputs.Linkage.Joints[^1].Id;
        editor.Actions.Select(id);
        editor.Actions.DeleteSelected();
        Assert.Null(editor.Current.Selection);
        Assert.Null(editor.Current.Inputs.Linkage.Joint(id));
    }
}
