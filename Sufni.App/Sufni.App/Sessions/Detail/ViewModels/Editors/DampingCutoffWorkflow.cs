using System;
using System.Threading.Tasks;
using System.Threading;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Shared.Models;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

/// <summary>Edits a cutoff preview against the current projected bike.</summary>
internal sealed class DampingCutoffWorkflow(
    Func<BikeSnapshot?> currentBike,
    Func<DampingSpeedCutoffs> currentCutoffs,
    Action<DampingSpeedCutoffs, bool> setCutoffs,
    IBikeCoordinator bikeCoordinator,
    Action<FailurePresentation> reportError,
    CancellationToken lifetime)
{
    public void Preview(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond)
    {
        if (lifetime.IsCancellationRequested || currentBike() is null) return;
        setCutoffs(currentCutoffs().With(side, circuit, DampingCutoffEditing.RoundDragValue(cutoffMmPerSecond)), false);
    }

    public void CancelPreview()
    {
        if (!lifetime.IsCancellationRequested) RestoreCutoffs(updatePlots: false);
    }

    public async Task CommitAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond)
    {
        if (lifetime.IsCancellationRequested || currentBike() is not { } bike) return;
        var committed = currentCutoffs().With(side, circuit, DampingCutoffEditing.RoundDragValue(cutoffMmPerSecond));
        setCutoffs(committed, true);
        var result = await bikeCoordinator.UpdateDampingSpeedCutoffAsync(bike.Id, bike.DampingSpeedCutoffs.Get(side, circuit), side, circuit, committed.Get(side, circuit));
        if (lifetime.IsCancellationRequested || currentBike()?.Id != bike.Id) return;
        switch (result)
        {
            case BikeDampingSpeedCutoffUpdateResult.Saved:
                break;
            case BikeDampingSpeedCutoffUpdateResult.Conflict:
                RestoreCutoffs(updatePlots: true);
                reportError(new FailurePresentation("Bike damping cutoff changed elsewhere. Try again with the latest values."));
                break;
            case BikeDampingSpeedCutoffUpdateResult.Missing:
                RestoreCutoffs(updatePlots: true);
                break;
            case BikeDampingSpeedCutoffUpdateResult.Failed failed:
                RestoreCutoffs(updatePlots: true);
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private void RestoreCutoffs(bool updatePlots) =>
        setCutoffs(currentBike()?.DampingSpeedCutoffs ?? DampingSpeedCutoffs.Default, updatePlots);
}
