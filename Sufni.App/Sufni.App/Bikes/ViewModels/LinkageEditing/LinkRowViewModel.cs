using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Bikes.ViewModels.Editors;

namespace Sufni.App.Bikes.ViewModels.LinkageEditing;

public sealed class LinkRowViewModel : ObservableObject
{
    private readonly BikeEditorActions actions;
    private readonly IReadOnlyList<JointRowViewModel> joints;
    private LinkageLink link;
    internal LinkRowViewModel(LinkageLink link, BikeEditorActions actions, LinkageWorkspaceViewModel workspace)
    {
        this.link = link;
        Workspace = workspace;
        this.actions = actions;
        this.joints = workspace.JointViewModels;
    }

    public LinkageWorkspaceViewModel Workspace { get; }
    public int Id => link.Id;
    public bool IsImmutable { get; private set; }
    public bool IsSelected { get; private set; }
    public double Length { get; private set; }
    public IReadOnlyList<JointRowViewModel> JointOptions => joints;
    public JointRowViewModel? A
    {
        get => joints.FirstOrDefault(row => row.Id == link.A);
        set { if (value?.Id != link.A) actions.LinkEnd(Id, true, value?.Id); }
    }
    public JointRowViewModel? B
    {
        get => joints.FirstOrDefault(row => row.Id == link.B);
        set { if (value?.Id != link.B) actions.LinkEnd(Id, false, value?.Id); }
    }
    public Point StartPoint => A is { } a ? new(a.X, a.Y) : default;
    public Point EndPoint => B is { } b ? new(b.X, b.Y) : default;
    public IBrush Brush => IsSelected ? Brushes.RoyalBlue : Brushes.CornflowerBlue;

    internal void Apply(LinkageLink value, BikeEditorState state)
    {
        link = value;
        IsImmutable = state.Inputs.Linkage.ShockId == Id;
        IsSelected = state.Selection == Id;
        Length = state.Geometry.LinkLengths.GetValueOrDefault(Id);
        OnPropertyChanged(string.Empty);
    }
}
