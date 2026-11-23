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
        controller.Actions.Prepared(draft, new BikeEditorImage(draft.ImageBytesMemory, new Size(200, 50)), true, true, false);
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
    public void OverlayBoundsAndLinkLengthsUseTheCurrentScale()
    {
        using var editor = Open(TestSnapshots.FullSuspensionLinkageSpec(true));
        var geometry = editor.Current.Geometry;
        Assert.Equal(2, geometry.Scale);
        Assert.Equal(351, geometry.Front.Diameter);
        Assert.Equal(350, geometry.Rear.Diameter);
        Assert.Equal(-169.5, geometry.Front.Left);
        Assert.Equal(-126, geometry.Front.Top);
        Assert.Equal(new Rect(-193, -146, 413, 391), geometry.ContentBounds);
        var link = editor.Current.Inputs.Linkage.Links[0];
        Assert.Equal(4, geometry.LinkLengths[link.Id]);
    }

    [Fact]
    public void ReorderedJointsAndLinksAndBaselineScaleDoNotDirtyTheDraft()
    {
        var spec = TestSnapshots.FullSuspensionLinkageSpec(true);
        using var editor = Open(spec);
        var baseline = editor.Current.Baseline with { PixelsToMillimeters = 1,
            RearSuspension = new RearSuspensionSpec.Linkage(new LinkageSpec([.. spec.Joints.Reverse()],
                [.. spec.Links.Reverse()], spec.Shock, spec.ShockStroke)) };
        editor.Actions.Saved(baseline);
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
