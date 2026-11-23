using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.LinkageEditing;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed partial class LinkageWorkspaceViewModel : BikeEditorWorkspace
{
    private readonly ObservableCollection<JointRowViewModel> joints = [];
    private readonly ObservableCollection<LinkRowViewModel> links = [];
    private readonly BikeImageCanvasViewModel canvas;
    private int? flyoutJoint;
    internal LinkageWorkspaceViewModel(BikeEditorStateController controller, BikeWheelsWorkspaceViewModel wheels,
        BikeImageCanvasViewModel canvas, IAsyncRelayCommand openImage) : base(controller)
    {
        Wheels = wheels;
        this.canvas = canvas;
        OpenImageCommand = openImage;
        JointViewModels = new(joints);
        LinkViewModels = new(links);
    }

    public IAsyncRelayCommand OpenImageCommand { get; }
    public BikeWheelsWorkspaceViewModel Wheels { get; }
    public BikeImageCanvasViewModel Canvas => canvas;
    public ReadOnlyObservableCollection<JointRowViewModel> JointViewModels { get; }
    public ReadOnlyObservableCollection<LinkRowViewModel> LinkViewModels { get; }
    public bool IsLinkageMode => State.Inputs.Mode == RearSuspensionKind.Linkage;
    public double? Chainstay { get => State.Inputs.Chainstay; set { if (value != Chainstay) Actions.Chainstay(value); } }
    public double? PixelsToMillimeters => State.Geometry.Scale;
    public double ImageRotationDegrees => State.Inputs.Rotation;
    public double CanvasWidth => State.Geometry.ContentBounds.Width;
    public double CanvasHeight => State.Geometry.ContentBounds.Height;
    public double ContentOffsetX => -State.Geometry.ContentBounds.X;
    public double ContentOffsetY => -State.Geometry.ContentBounds.Y;
    public double RotatedImageLeft => State.Geometry.ImageBounds.X;
    public double RotatedImageTop => State.Geometry.ImageBounds.Y;
    public IList<Point> RearAxlePath => State.Geometry.RearAxlePath;
    public bool OverlayVisible { get => State.OverlayVisible; set { if (value != OverlayVisible) Actions.Overlay(value); } }

    public double LinkStrokeThickness { get; private set; } = 10;
    public double JointFontSize { get; private set; } = 24;
    public void UpdateZoomPresentation(double zoom)
    {
        LinkStrokeThickness = 45 / zoom;
        JointFontSize = 150 * (0.85 + 0.15 * (1 - 1 / zoom)) / zoom;
        OnPropertyChanged(nameof(JointFontSize));
        OnPropertyChanged(nameof(LinkStrokeThickness));
    }

    public JointRowViewModel? SelectedPoint
    {
        get => joints.FirstOrDefault(joint => joint.Id == State.Selection);
        set { if (value != SelectedPoint) Actions.Select(value?.Id); }
    }
    public LinkRowViewModel? SelectedLink
    {
        get => links.FirstOrDefault(link => link.Id == State.Selection);
        set { if (value != SelectedLink) Actions.Select(value?.Id); }
    }

    public void AddPoint(double x, double y)
    {
        if (State.Phase != BikeEditorPhase.Ready) return;
        flyoutJoint = State.Inputs.Linkage.NextId;
        Actions.AddJoint(x, y);
    }
    [RelayCommand] private void ClearSelection() => Actions.Select(null);
    [RelayCommand] private void CreateJoint() => Actions.AddJoint();
    [RelayCommand] private void CreateLink() => Actions.AddLink();
    [RelayCommand] private void SelectPoint(JointRowViewModel point) => Actions.Select(point.Id);
    [RelayCommand] private void SelectLink(LinkRowViewModel link) => Actions.Select(link.Id);
    private bool CanDelete() => LinkageIntentReducers.CanDelete(State.Inputs.Linkage, State.Selection);
    [RelayCommand(CanExecute = nameof(CanDelete))] private void DeleteSelectedItem() => Actions.DeleteSelected();

    internal override void Apply(BikeEditorState state)
    {
        var draft = state.Inputs.Linkage;
        foreach (var row in links.Where(row => draft.Link(row.Id) is null).ToArray()) links.Remove(row);
        foreach (var row in joints.Where(row => draft.Joint(row.Id) is null).ToArray()) joints.Remove(row);
        foreach (var joint in draft.Joints)
        {
            var row = joints.FirstOrDefault(row => row.Id == joint.Id);
            if (row is null)
            {
                row = new(joint, Actions, this) { ShowFlyout = joint.Id == flyoutJoint };
                joints.Add(row);
            }
            row.Apply(joint, joint.Id == state.Selection);
        }
        foreach (var link in draft.Links)
        {
            var row = links.FirstOrDefault(row => row.Id == link.Id);
            if (row is null) { row = new(link, Actions, this); links.Add(row); }
            row.Apply(link, state);
        }
        flyoutJoint = null;
        base.Apply(state);
        DeleteSelectedItemCommand.NotifyCanExecuteChanged();
    }
}
