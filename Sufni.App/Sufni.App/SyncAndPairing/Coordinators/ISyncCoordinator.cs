using System;
using System.Threading.Tasks;
using System.Threading;

using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.SyncAndPairing.Coordinators;

public interface ISyncCoordinator
{
    bool IsRunning { get; }
    bool IsPaired { get; }
    bool CanSync { get; }
    SynchronizationProgressSnapshot? Progress { get; }

    event EventHandler? IsRunningChanged;
    event EventHandler? IsPairedChanged;
    event EventHandler? CanSyncChanged;
    event EventHandler? ProgressChanged;
    event EventHandler<SyncCompletedEventArgs>? SyncCompleted;
    event EventHandler<SyncFailedEventArgs>? SyncFailed;

    Task SyncAllAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
