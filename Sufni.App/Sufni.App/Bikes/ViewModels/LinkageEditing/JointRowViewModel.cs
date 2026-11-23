using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.LinkageEditing;

public sealed class JointRowViewModel : ObservableObject
{
    private readonly BikeEditorActions actions;
    private LinkageJoint joint;
    internal JointRowViewModel(LinkageJoint joint, BikeEditorActions actions, LinkageWorkspaceViewModel workspace)
    {
        this.joint = joint;
        Workspace = workspace;
        this.actions = actions;
    }

    public LinkageWorkspaceViewModel Workspace { get; }
    public int Id => joint.Id;
    public JointEditRule EditRule => JointEditRules.For(joint);
    public bool ShowFlyout { get; set; }
    public bool IsSelected { get; private set; }
    public double X { get => joint.X; set { if (value != X) actions.Move(Id, value, Y); } }
    public double Y { get => joint.Y; set { if (value != Y) actions.Move(Id, X, value); } }
    public string Name { get => joint.Name; set { if (value != Name) actions.Rename(Id, value); } }
    public JointType? Type { get => joint.Type; set { if (value != Type) actions.JointType(Id, value); } }
    public static IReadOnlyList<JointType?> PointTypes { get; } = [null, JointType.Fixed, JointType.Floating, JointType.HeadTube];
    public IBrush Brush => Type switch
    {
        JointType.FrontWheel => Brushes.Cyan, JointType.RearWheel => Brushes.Cyan,
        JointType.BottomBracket => Brushes.Purple, JointType.HeadTube => Brushes.Lime,
        JointType.Fixed => Brushes.OrangeRed, _ => Brushes.HotPink
    };

    internal void Apply(LinkageJoint value, bool selected)
    {
        joint = value;
        IsSelected = selected;
        OnPropertyChanged(string.Empty);
    }
}
