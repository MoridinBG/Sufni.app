using Avalonia.Headless.XUnit;
using Sufni.Kinematics;

using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.ViewModels.LinkageEditing;

[Collection("Ui")]
public class LinkageEditorViewModelTests
{
    private const string DetachedPointName = "Detached point";

    [AvaloniaFact]
    public void Load_RoundTripsBaselineLinkage_WithoutJointOrLinkDifferences()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var baselineSpec = baseline.ToSpec();
        var viewModel = new LinkageEditorViewModel();

        viewModel.Load(baselineSpec, imageHeight: 100, pixelsToMillimeters: 1);

        Assert.Equal(baseline.Joints.Count, viewModel.JointViewModels.Count);
        Assert.Equal(baseline.Links.Count + 1, viewModel.LinkViewModels.Count);
        Assert.False(viewModel.HasChangesComparedTo(baselineSpec, 100, 1));

        var rebuilt = viewModel.BuildCurrentLinkageSpec(100, 1, baseline.ShockStroke);

        Assert.NotNull(rebuilt);
        Assert.Equal(baseline.Joints.Count, rebuilt.Joints.Count);
        Assert.Equal(baseline.Links.Count, rebuilt.Links.Count);
        Assert.Equal(baseline.ShockStroke, rebuilt.ShockStroke);
    }

    [AvaloniaFact]
    public void AddInitialJoints_AddsMandatoryJoints_AndDoesNotDuplicateShock()
    {
        var viewModel = new LinkageEditorViewModel();
        var mapping = new JointNameMapping();

        viewModel.AddInitialJoints();
        viewModel.AddInitialJoints();

        Assert.Equal(7, viewModel.JointViewModels.Count);
        Assert.Single(viewModel.LinkViewModels);
        Assert.Contains(viewModel.JointViewModels, joint => joint.Name == mapping.FrontWheel);
        Assert.Contains(viewModel.JointViewModels, joint => joint.Name == mapping.BottomBracket);
        Assert.Contains(viewModel.JointViewModels, joint => joint.Name == mapping.RearWheel);
        Assert.Contains(viewModel.JointViewModels, joint => joint.Name == mapping.HeadTube1);
        Assert.Contains(viewModel.JointViewModels, joint => joint.Name == mapping.HeadTube2);
        Assert.Contains(viewModel.JointViewModels, joint => joint.Name == mapping.ShockEye1);
        Assert.Contains(viewModel.JointViewModels, joint => joint.Name == mapping.ShockEye2);
        Assert.Equal("Shock", viewModel.LinkViewModels[0].Name);
    }

    [AvaloniaFact]
    public void Load_NullLinkage_ClearsExistingState()
    {
        var viewModel = new LinkageEditorViewModel();
        viewModel.AddInitialJoints();

        viewModel.Load(null, imageHeight: 100, pixelsToMillimeters: 1);

        Assert.Empty(viewModel.JointViewModels);
        Assert.Empty(viewModel.LinkViewModels);
        Assert.False(viewModel.HasChangesComparedTo(null, 100, 1));
    }

    [AvaloniaFact]
    public void Selection_ClearsPreviousSelection_WhenSwitchingBetweenPointAndLink()
    {
        var viewModel = new LinkageEditorViewModel();
        viewModel.AddInitialJoints();
        var point = Assert.Single(viewModel.JointViewModels, joint => joint.Type == JointType.FrontWheel);
        var link = Assert.Single(viewModel.LinkViewModels, item => item.Name == "Shock");

        viewModel.SelectedPoint = point;

        Assert.Same(point, viewModel.SelectedPoint);
        Assert.True(point.IsSelected);
        Assert.Null(viewModel.SelectedLink);

        viewModel.SelectedLink = link;

        Assert.Null(viewModel.SelectedPoint);
        Assert.Same(link, viewModel.SelectedLink);
        Assert.False(point.IsSelected);
        Assert.True(link.IsSelected);
    }

    [AvaloniaFact]
    public void CreateLinkCommand_AddsEditableLink()
    {
        var viewModel = new LinkageEditorViewModel();

        viewModel.CreateLinkCommand.Execute(null);

        var link = Assert.Single(viewModel.LinkViewModels);
        Assert.False(link.IsImmutable);
        Assert.Null(link.A);
        Assert.Null(link.B);
    }

    [AvaloniaFact]
    public void RotateAll_RotatesAllJointCoordinates()
    {
        var viewModel = new LinkageEditorViewModel();
        viewModel.AddInitialJoints();

        var frontWheel = Assert.Single(viewModel.JointViewModels, joint => joint.Type == JointType.FrontWheel);

        viewModel.RotateAll(90);

        Assert.Equal(-150, frontWheel.X, 3);
        Assert.Equal(100, frontWheel.Y, 3);
    }

    [AvaloniaFact]
    public void RemovingJoint_DetachesPropertyHandler_FromRemovedInstance()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var baselineSpec = WithDetachedPoint(baseline.ToSpec());

        var viewModel = new LinkageEditorViewModel();
        var previewChanges = 0;
        var stateChanges = 0;
        viewModel.PreviewChanged += (_, _) => previewChanges++;
        viewModel.StateChanged += (_, _) => stateChanges++;

        viewModel.Load(baselineSpec, imageHeight: 100, pixelsToMillimeters: 1);
        var removedJoint = Assert.Single(viewModel.JointViewModels, joint => joint.Name == DetachedPointName);
        viewModel.SelectedPoint = removedJoint;
        viewModel.DeleteSelectedItemCommand.Execute(null);
        previewChanges = 0;
        stateChanges = 0;

        removedJoint.Name = $"{DetachedPointName} renamed";

        Assert.Equal(0, previewChanges);
        Assert.Equal(0, stateChanges);
    }

    [AvaloniaFact]
    public void RemovingLink_DetachesPropertyHandler_FromRemovedInstance()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var baselineSpec = WithDetachedLink(baseline.ToSpec());

        var viewModel = new LinkageEditorViewModel();
        var previewChanges = 0;
        var stateChanges = 0;
        viewModel.PreviewChanged += (_, _) => previewChanges++;
        viewModel.StateChanged += (_, _) => stateChanges++;

        viewModel.Load(baselineSpec, imageHeight: 100, pixelsToMillimeters: 1);
        var removedLink = Assert.Single(
            viewModel.LinkViewModels,
            link => link.A?.Name == baseline.Joints[0].Name && link.B?.Name == DetachedPointName);
        viewModel.SelectedLink = removedLink;
        viewModel.DeleteSelectedItemCommand.Execute(null);
        previewChanges = 0;
        stateChanges = 0;

        removedLink.A = viewModel.JointViewModels[2];

        Assert.Equal(0, previewChanges);
        Assert.Equal(0, stateChanges);
    }

    [AvaloniaFact]
    public void MovingJoint_RaisesPreviewChanged_WithMovedJoint()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var baselineSpec = baseline.ToSpec();
        var viewModel = new LinkageEditorViewModel();
        LinkagePreviewChangedEventArgs? previewArgs = null;
        var stateChanges = 0;
        viewModel.PreviewChanged += (_, args) => previewArgs = args;
        viewModel.StateChanged += (_, _) => stateChanges++;

        viewModel.Load(baselineSpec, imageHeight: 100, pixelsToMillimeters: 1);
        previewArgs = null;
        stateChanges = 0;

        var joint = Assert.Single(viewModel.JointViewModels, item => item.Type == JointType.FrontWheel);
        joint.X += 10;

        Assert.NotNull(previewArgs);
        Assert.Same(joint, previewArgs!.Joint);
        Assert.Equal(0, stateChanges);
    }

    [AvaloniaFact]
    public void WasPossiblyDragged_RaisesStateChanged_WithoutPreviewChanged()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var baselineSpec = baseline.ToSpec();
        var viewModel = new LinkageEditorViewModel();
        var previewChanges = 0;
        var stateChanges = 0;
        viewModel.PreviewChanged += (_, _) => previewChanges++;
        viewModel.StateChanged += (_, _) => stateChanges++;

        viewModel.Load(baselineSpec, imageHeight: 100, pixelsToMillimeters: 1);
        previewChanges = 0;
        stateChanges = 0;

        var joint = Assert.Single(viewModel.JointViewModels, item => item.Type == JointType.FrontWheel);

        joint.WasPossiblyDragged = true;

        Assert.Equal(0, previewChanges);
        Assert.Equal(1, stateChanges);
    }

    [AvaloniaFact]
    public void Selection_DoesNotRaisePreviewOrStateChanged()
    {
        var viewModel = new LinkageEditorViewModel();
        var previewChanges = 0;
        var stateChanges = 0;
        viewModel.PreviewChanged += (_, _) => previewChanges++;
        viewModel.StateChanged += (_, _) => stateChanges++;

        viewModel.AddInitialJoints();
        previewChanges = 0;
        stateChanges = 0;

        viewModel.SelectedPoint = Assert.Single(viewModel.JointViewModels, joint => joint.Type == JointType.FrontWheel);

        Assert.Equal(0, previewChanges);
        Assert.Equal(0, stateChanges);
    }

    [AvaloniaFact]
    public void DeleteSelectedItemCommand_RemovesSelectedPoint_AndConnectedLinks()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var baselineSpec = WithDetachedLink(baseline.ToSpec());
        var viewModel = new LinkageEditorViewModel();

        viewModel.Load(baselineSpec, imageHeight: 100, pixelsToMillimeters: 1);
        var point = Assert.Single(viewModel.JointViewModels, joint => joint.Name == DetachedPointName);

        viewModel.SelectedPoint = point;
        viewModel.DeleteSelectedItemCommand.Execute(null);

        Assert.DoesNotContain(viewModel.JointViewModels, joint => joint.Name == DetachedPointName);
        Assert.DoesNotContain(viewModel.LinkViewModels, link => link.A?.Name == DetachedPointName || link.B?.Name == DetachedPointName);
    }

    [AvaloniaFact]
    public void HasChangesComparedTo_ReturnsTrue_WhenShockEndpointsChange()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var baselineSpec = baseline.ToSpec();
        var viewModel = new LinkageEditorViewModel();

        viewModel.Load(baselineSpec, imageHeight: 100, pixelsToMillimeters: 1);
        var shockLink = Assert.Single(
            viewModel.LinkViewModels,
            link => link.A?.Name == baseline.Shock.A_Name && link.B?.Name == baseline.Shock.B_Name);
        shockLink.A = Assert.Single(viewModel.JointViewModels, joint => joint.Type == JointType.BottomBracket);

        Assert.True(viewModel.HasChangesComparedTo(baselineSpec, 100, 1));
    }

    private static LinkageSpec WithDetachedPoint(LinkageSpec baseline) =>
        new(
            [.. baseline.Joints, new JointSpec(DetachedPointName, JointType.Floating, 6, 6)],
            baseline.Links,
            baseline.Shock,
            baseline.ShockStroke);

    private static LinkageSpec WithDetachedLink(LinkageSpec baseline) =>
        new(
            [.. baseline.Joints, new JointSpec(DetachedPointName, JointType.Floating, 6, 6)],
            [.. baseline.Links, new LinkSpec(baseline.Joints[0].Name, DetachedPointName)],
            baseline.Shock,
            baseline.ShockStroke);
}
