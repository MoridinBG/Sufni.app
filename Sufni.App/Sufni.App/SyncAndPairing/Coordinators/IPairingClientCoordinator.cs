using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>Owns mobile pairing, installation identity and peer discovery.</summary>
public interface IPairingClientCoordinator
{
    string? DeviceId { get; }
    string? DisplayName { get; }
    string? ServerUrl { get; }
    PairingClientState State { get; }
    IObservable<PairingClientState> States { get; }
    event EventHandler? DeviceIdChanged;
    event EventHandler? DisplayNameChanged;
    event EventHandler? ServerUrlChanged;
    Task InitializeAsync();
    void StartBrowsing();
    void StopBrowsing();
    Task<ISynchronizationPeer?> ResolvePeerAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<RequestPairingResult> RequestPairingAsync(string? displayName);
    Task<ConfirmPairingResult> ConfirmPairingAsync(string? displayName, string pin);
    Task<UnpairResult> UnpairAsync();
}

public abstract record RequestPairingResult
{
    private RequestPairingResult() { }
    public sealed record Sent : RequestPairingResult;
    public sealed record ServerUnavailable : RequestPairingResult;
    public sealed record Failed(PairingFailureReason Reason, string? Details = null) : RequestPairingResult;
}

public abstract record ConfirmPairingResult
{
    private ConfirmPairingResult() { }
    public sealed record Paired : ConfirmPairingResult;
    public sealed record PinRejected : ConfirmPairingResult;
    public sealed record Failed(PairingFailureReason Reason, string? Details = null) : ConfirmPairingResult;
}

public abstract record UnpairResult
{
    private UnpairResult() { }
    public sealed record Unpaired : UnpairResult;
    // Network call failed but local credentials were already cleared,
    // matching the existing "could unpair only locally" behaviour.
    public sealed record LocalOnly : UnpairResult;
    public sealed record Failed(PairingFailureReason Reason, string? Details = null) : UnpairResult;
}
