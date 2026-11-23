using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;

namespace Sufni.App.Bikes.ViewModels.Editors;

internal sealed class RearSuspensionModeSwitch(BikeEditorStateController controller, bool pointer, IDialogService dialogs)
{
    public bool CanChange => pointer && controller.Current.Phase == BikeEditorPhase.Ready;

    public async Task ChangeAsync(RearSuspensionKind mode)
    {
        var state = controller.Current;
        var previous = state.Inputs.Mode;
        if (!CanChange || previous == mode) return;
        var confirm = previous switch
        {
            RearSuspensionKind.Linkage => state.Geometry.Spec is RearSuspensionSpec.Linkage ||
                state.Inputs.Image is not null || state.Inputs.Chainstay is not null,
            RearSuspensionKind.LeverageRatio => state.Inputs.Curve is not null,
            _ => false
        };
        if (confirm && !await dialogs.ShowConfirmationAsync("Discard rear suspension data?",
            $"Switching to {Label(mode).ToLowerInvariant()} will discard the current {Label(previous).ToLowerInvariant()} data. Continue?")) return;
        if (CanChange && controller.Current.Inputs.Mode == previous) controller.Actions.Mode(mode);
    }

    public static string Label(RearSuspensionKind mode) => mode switch
    {
        RearSuspensionKind.Linkage => "Linkage",
        RearSuspensionKind.LeverageRatio => "Leverage ratio",
        _ => "Hardtail"
    };
}

public sealed class RearSuspensionWorkspaceViewModel : BikeEditorWorkspace
{
    private readonly RearSuspensionModeSwitch modeSwitch;
    internal RearSuspensionWorkspaceViewModel(BikeEditorStateController controller, RearSuspensionModeSwitch modeSwitch) : base(controller)
    {
        this.modeSwitch = modeSwitch;
        SetRearSuspensionModeCommand = new AsyncRelayCommand<RearSuspensionKind>(modeSwitch.ChangeAsync, _ => modeSwitch.CanChange);
    }

    public IAsyncRelayCommand<RearSuspensionKind> SetRearSuspensionModeCommand { get; }
    public RearSuspensionKind RearSuspensionMode => State.Inputs.Mode;
    public bool IsHardtailMode => RearSuspensionMode == RearSuspensionKind.None;
    public bool IsLinkageMode => RearSuspensionMode == RearSuspensionKind.Linkage;
    public bool IsLeverageRatioMode => RearSuspensionMode == RearSuspensionKind.LeverageRatio;
    public string RearSuspensionModeDisplayName => RearSuspensionModeSwitch.Label(RearSuspensionMode);
    public bool CanChangeRearSuspensionMode => modeSwitch.CanChange;
    internal override void Apply(BikeEditorState state)
    {
        base.Apply(state);
        SetRearSuspensionModeCommand.NotifyCanExecuteChanged();
    }
}
