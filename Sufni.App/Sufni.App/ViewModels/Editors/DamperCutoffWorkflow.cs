using System;
using System.Threading.Tasks;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.SessionDetails;
using Sufni.App.Views.Plots;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Owns the damping-cutoff preview/commit state machine for a recorded
/// session: preview stages rounded values on the context, cancel restores
/// the preview origin, commit persists through the bike coordinator and
/// reconciles the Saved/Conflict/Failed outcome back onto the context.
/// </summary>
internal sealed class DamperCutoffWorkflow(
    RecordedSessionContext context,
    IBikeCoordinator? bikeCoordinator,
    Action<string> reportError)
{
    private DampingSpeedCutoffOwner? owner;
    private DampingSpeedCutoffs persistedCutoffs = DampingSpeedCutoffs.Default;
    private DampingSpeedCutoffs? previewOrigin;

    public bool CanEdit => owner is not null;

    public void ApplyContext(DampingSpeedCutoffs cutoffs, DampingSpeedCutoffOwner? cutoffOwner)
    {
        persistedCutoffs = cutoffs.ClampValues();
        previewOrigin = null;
        owner = cutoffOwner;
        context.CanEditDampingSpeedCutoffs = owner is not null;
        context.PlotDampingSpeedCutoffs = persistedCutoffs;
        context.DampingSpeedCutoffs = persistedCutoffs;
    }

    public void Preview(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond)
    {
        if (owner is null)
        {
            return;
        }

        previewOrigin ??= context.DampingSpeedCutoffs;
        context.DampingSpeedCutoffs = context.DampingSpeedCutoffs.With(
            side,
            circuit,
            DampingCutoffInteraction.RoundDragValue(cutoffMmPerSecond));
    }

    public void CancelPreview()
    {
        if (previewOrigin is not { } origin)
        {
            return;
        }

        previewOrigin = null;
        context.DampingSpeedCutoffs = origin;
    }

    public async Task CommitAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond)
    {
        if (owner is not { } cutoffOwner || bikeCoordinator is null)
        {
            return;
        }

        previewOrigin = null;
        var committedCutoffs = context.DampingSpeedCutoffs.With(
            side,
            circuit,
            DampingCutoffInteraction.RoundDragValue(cutoffMmPerSecond));
        context.DampingSpeedCutoffs = committedCutoffs;
        context.PlotDampingSpeedCutoffs = committedCutoffs;

        var result = await bikeCoordinator.UpdateDampingSpeedCutoffAsync(
            cutoffOwner.BikeId,
            cutoffOwner.BaselineUpdated,
            side,
            circuit,
            committedCutoffs.Get(side, circuit));

        switch (result)
        {
            case BikeDampingSpeedCutoffUpdateResult.Saved saved:
                ApplyContext(
                    saved.Snapshot.DampingSpeedCutoffs,
                    new DampingSpeedCutoffOwner(saved.Snapshot.Id, saved.Snapshot.Updated));
                break;

            case BikeDampingSpeedCutoffUpdateResult.Conflict conflict:
                ApplyContext(
                    conflict.CurrentSnapshot.DampingSpeedCutoffs,
                    new DampingSpeedCutoffOwner(conflict.CurrentSnapshot.Id, conflict.CurrentSnapshot.Updated));
                reportError("Bike damping cutoff changed elsewhere. Reloaded the latest cutoff.");
                break;

            case BikeDampingSpeedCutoffUpdateResult.Failed failed:
                context.DampingSpeedCutoffs = persistedCutoffs;
                context.PlotDampingSpeedCutoffs = persistedCutoffs;
                reportError($"Could not save damping cutoff: {failed.ErrorMessage}");
                break;
        }
    }
}
