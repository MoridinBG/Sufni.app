using System;
using System.Threading.Tasks;
using Sufni.App.Services;

namespace Sufni.App.Coordinators;

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

    Task SyncAllAsync();
}
