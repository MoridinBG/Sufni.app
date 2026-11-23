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
    public double? HeadAngle { get => State.Geometry.EffectiveHeadAngle; set { if (value != HeadAngle) Actions.HeadAngle(value); } }
    public double? ForksStroke { get => State.Inputs.ForkStroke; set { if (value != ForksStroke) Actions.ForkStroke(value); } }
    public double? ShockStroke { get => State.Inputs.ShockStroke; set { if (value != ShockStroke) Actions.ShockStroke(value); } }
}
