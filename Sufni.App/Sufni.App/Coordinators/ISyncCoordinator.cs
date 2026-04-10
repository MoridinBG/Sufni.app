using System;
using System.Threading.Tasks;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns the outbound-sync workflow: pulling/pushing entities via the
/// HTTP API and refreshing the stores after a successful round-trip.
/// Singleton — registered in the shared composition root because its
/// dependencies are nullable and it tolerates desktop. Does <b>not</b>
/// own inbound-sync arrival; bikes/setups arrive on
/// <see cref="IInboundSyncCoordinator"/>, sessions on
/// <see cref="ISessionCoordinator"/>, paired devices on
/// <see cref="IPairedDeviceCoordinator"/>.
/// </summary>
public interface ISyncCoordinator
{
    bool IsRunning { get; }
    bool IsPaired { get; }
    bool CanSync { get; }
    event EventHandler? IsRunningChanged;
    event EventHandler? IsPairedChanged;
    event EventHandler? CanSyncChanged;
    event EventHandler<SyncCompletedEventArgs>? SyncCompleted;
    event EventHandler<SyncFailedEventArgs>? SyncFailed;

    Task SyncAllAsync();
}

public sealed record SyncCompletedEventArgs(string Message);
public sealed record SyncFailedEventArgs(string ErrorMessage);
