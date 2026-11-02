using System;
using System.Threading.Tasks;
using System.Threading;

using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Coordinators;

public interface ISyncCoordinator
{
    SyncState State { get; }
    IObservable<SyncState> States { get; }
    Task<ConfirmPairingResult> ConfirmPairingAndSyncAsync(string? displayName, string pin,
        IProgress<PairingClientState>? progress = null);
    event EventHandler<SyncCompletedEventArgs>? SyncCompleted;
    event EventHandler<SyncFailedEventArgs>? SyncFailed;
    event EventHandler? ServerUnavailable;

    Task SyncAllAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
