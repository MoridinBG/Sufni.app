using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.SyncAndPairing.Models;

public enum PairingClientState { Unpaired, AwaitingPin, Paired }

/// <summary>Pairing availability and the current synchronization operation.</summary>
public sealed record SyncState(PairingClientState Pairing, SyncActivity Activity,
    SynchronizationProgressSnapshot? Progress = null)
{
    public bool IsPaired => Pairing == PairingClientState.Paired;
    public bool IsRequestSent => Pairing == PairingClientState.AwaitingPin;
    public bool IsRunning => Activity is SyncActivity.Outbound or SyncActivity.Inbound;
    public bool CanSync => IsPaired && Activity == SyncActivity.Idle;
}
