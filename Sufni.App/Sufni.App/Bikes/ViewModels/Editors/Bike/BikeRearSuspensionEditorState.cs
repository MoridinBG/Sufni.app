
using Sufni.App.Bikes.Models;
namespace Sufni.App.Bikes.ViewModels.Editors.Bike;

public abstract record BikeRearSuspensionEditorState
{
    public sealed record Hardtail : BikeRearSuspensionEditorState;

    public sealed record Linkage(LinkageRearSuspension Value) : BikeRearSuspensionEditorState;

    public sealed record LeverageRatio(LeverageRatioRearSuspension Value) : BikeRearSuspensionEditorState;

    public sealed record DraftLinkage : BikeRearSuspensionEditorState;

    public sealed record DraftLeverageRatio : BikeRearSuspensionEditorState;

    public sealed record Invalid(RearSuspensionResolutionError Error) : BikeRearSuspensionEditorState;
}
