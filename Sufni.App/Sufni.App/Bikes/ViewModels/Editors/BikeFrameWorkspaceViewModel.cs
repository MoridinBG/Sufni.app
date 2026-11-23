using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Bikes.Models;

namespace Sufni.App.Bikes.ViewModels.Editors;

public abstract class BikeEditorWorkspace(BikeEditorStateController controller) : ObservableObject
{
    protected BikeEditorState State { get; private set; } = controller.Current;
    internal BikeEditorPhase Phase => State.Phase;
    protected BikeEditorActions Actions { get; } = controller.Actions;
    internal virtual void Apply(BikeEditorState state)
    {
        State = state;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class BikeFrameWorkspaceViewModel(BikeEditorStateController controller) : BikeEditorWorkspace(controller)
{
    public bool HasRearSuspension => State.Inputs.Mode != RearSuspensionKind.None;
    public bool IsHeadAngleDerived => State.Geometry.IsHeadAngleDerived;
    public string HeadAngleHint => IsHeadAngleDerived
        ? "Calculated from the two head-tube points and the ground line between the wheel contact points."
        : "Enter the head angle manually. Automatic calculation requires both head-tube points and a calibrated ground line.";
    public double? HeadAngle { get => State.Geometry.EffectiveHeadAngle; set { if (value != HeadAngle) Actions.HeadAngle(value); } }
    public double? ForksStroke { get => State.Inputs.ForkStroke; set { if (value != ForksStroke) Actions.ForkStroke(value); } }
    public double? ShockStroke { get => State.Inputs.ShockStroke; set { if (value != ShockStroke) Actions.ShockStroke(value); } }
}
