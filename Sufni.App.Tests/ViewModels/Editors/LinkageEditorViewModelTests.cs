using Avalonia.Headless.XUnit;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.LinkageEditing;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.Kinematics;

namespace Sufni.App.Tests.ViewModels.Editors;

public class LinkageEditorViewModelTests
{
    [AvaloniaFact]
    public void Load_RoundTripsBaselineLinkage_WithoutJointOrLinkDifferences()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var viewModel = new LinkageEditorViewModel();

        viewModel.Load(baseline, imageHeight: 100, pixelsToMillimeters: 1);

        Assert.Equal(baseline.Joints.Count, viewModel.JointViewModels.Count);
        Assert.Equal(baseline.Links.Count + 1, viewModel.LinkViewModels.Count);
        Assert.False(viewModel.HasChangesComparedTo(baseline, 100, 1));

        var rebuilt = viewModel.BuildCurrentLinkage(100, 1, baseline.ShockStroke);

        Assert.NotNull(rebuilt);
        Assert.Equal(baseline.Joints.Count, rebuilt.Joints.Count);
        Assert.Equal(baseline.Links.Count, rebuilt.Links.Count);
        Assert.Equal(baseline.ShockStroke, rebuilt.ShockStroke);
    }

    [AvaloniaFact]
    public void BuildCurrentLinkage_ResolvesLinkAndShockLengths()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var viewModel = new LinkageEditorViewModel();

        viewModel.Load(baseline, imageHeight: 100, pixelsToMillimeters: 1);

        var rebuilt = viewModel.BuildCurrentLinkage(100, 1, baseline.ShockStroke);

        Assert.NotNull(rebuilt);
        Assert.All(rebuilt.Links, link => Assert.True(link.Length > 0));
        Assert.True(rebuilt.Shock.Length > 0);
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
        baseline.Joints.Add(new Joint("Detached point", JointType.Floating, 6, 6));
        baseline.ResolveJoints();

        var viewModel = new LinkageEditorViewModel();
        var changes = new List<LinkageEditorChange>();
        viewModel.Changed += (_, change) => changes.Add(change);

        viewModel.Load(baseline, imageHeight: 100, pixelsToMillimeters: 1);
        var removedJoint = Assert.Single(viewModel.JointViewModels, joint => joint.Name == "Detached point");
        viewModel.SelectedPoint = removedJoint;
        viewModel.DeleteSelectedItemCommand.Execute(null);
        changes.Clear();

        removedJoint.Name = "Detached point renamed";

        Assert.Empty(changes);
    }

    [AvaloniaFact]
    public void RemovingLink_DetachesPropertyHandler_FromRemovedInstance()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var detachedPoint = new Joint("Detached point", JointType.Floating, 6, 6);
        baseline.Joints.Add(detachedPoint);
        baseline.Links.Add(new Link(baseline.Joints[0], detachedPoint));
        baseline.ResolveJoints();

        var viewModel = new LinkageEditorViewModel();
        var changes = new List<LinkageEditorChange>();
        viewModel.Changed += (_, change) => changes.Add(change);

        viewModel.Load(baseline, imageHeight: 100, pixelsToMillimeters: 1);
        var removedLink = Assert.Single(
            viewModel.LinkViewModels,
            link => link.A?.Name == baseline.Joints[0].Name && link.B?.Name == detachedPoint.Name);
        viewModel.SelectedLink = removedLink;
        viewModel.DeleteSelectedItemCommand.Execute(null);
        changes.Clear();

        removedLink.A = viewModel.JointViewModels[2];

        Assert.Empty(changes);
    }

    [AvaloniaFact]
    public void DeleteSelectedItemCommand_RemovesSelectedPoint_AndConnectedLinks()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var detachedPoint = new Joint("Detached point", JointType.Floating, 6, 6);
        baseline.Joints.Add(detachedPoint);
        baseline.Links.Add(new Link(baseline.Joints[0], detachedPoint));
        baseline.ResolveJoints();
        var viewModel = new LinkageEditorViewModel();

        viewModel.Load(baseline, imageHeight: 100, pixelsToMillimeters: 1);
        var point = Assert.Single(viewModel.JointViewModels, joint => joint.Name == detachedPoint.Name);

        viewModel.SelectedPoint = point;
        viewModel.DeleteSelectedItemCommand.Execute(null);

        Assert.DoesNotContain(viewModel.JointViewModels, joint => joint.Name == detachedPoint.Name);
        Assert.DoesNotContain(viewModel.LinkViewModels, link => link.A?.Name == detachedPoint.Name || link.B?.Name == detachedPoint.Name);
    }

    [AvaloniaFact]
    public void HasChangesComparedTo_ReturnsTrue_WhenShockEndpointsChange()
    {
        var baseline = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var viewModel = new LinkageEditorViewModel();

        viewModel.Load(baseline, imageHeight: 100, pixelsToMillimeters: 1);
        var shockLink = Assert.Single(
            viewModel.LinkViewModels,
            link => link.A?.Name == baseline.Shock.A_Name && link.B?.Name == baseline.Shock.B_Name);
        shockLink.A = Assert.Single(viewModel.JointViewModels, joint => joint.Type == JointType.BottomBracket);

        Assert.True(viewModel.HasChangesComparedTo(baseline, 100, 1));
    }
}