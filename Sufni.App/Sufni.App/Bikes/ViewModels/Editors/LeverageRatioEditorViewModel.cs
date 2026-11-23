using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed partial class LeverageRatioEditorViewModel(BikeEditorStateController controller, IBikeEditorService files,
    bool pointer, Action<FailurePresentation> error) : BikeEditorWorkspace(controller), IDisposable
{
    private readonly CancellableOperation operation = new();
    public bool CanEdit => pointer && State.Phase == BikeEditorPhase.Ready && State.Inputs.Mode == RearSuspensionKind.LeverageRatio;
    public LeverageRatioSpec? Value => State.Inputs.Curve;
    public IReadOnlyList<LeverageRatioPoint> PointsView => Value?.Points ?? [];
    public int PointCount => PointsView.Count;
    public double? MaxShockStroke => Value?.MaxShockStroke;
    public double? MaxWheelTravel => Value?.MaxWheelTravel;
    public IReadOnlyList<string> ValidationErrors => State.Inputs.ImportErrors;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ImportLeverageRatio()
    {
        var token = operation.Start();
        try
        {
            var result = await files.ImportLeverageRatioAsync(token);
            if (token.IsCancellationRequested || !CanEdit) return;
            if (result is LeverageRatioImportResult.Failed failed) error(FailurePresentation.From(failed.Reason, failed.Details));
            else Actions.ImportCurve(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private bool CanClear() => CanEdit && (Value is not null || ValidationErrors.Count > 0);
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => Actions.ClearCurve();

    internal override void Apply(BikeEditorState state)
    {
        base.Apply(state);
        if (!CanEdit) operation.Cancel();
        ClearCommand.NotifyCanExecuteChanged();
        ImportLeverageRatioCommand.NotifyCanExecuteChanged();
    }

    public void Cancel() => operation.Cancel();
    public void Dispose() => operation.Dispose();
}
