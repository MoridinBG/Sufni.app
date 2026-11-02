using System;
using System.Threading.Tasks;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Services;

/// <summary>Pairing and discovery binding for peer-scoped HTTP transports.</summary>
public interface IHttpApiService
{
    ISynchronizationPeer? PairedPeer { get; }
    IObservable<ISynchronizationPeer?> PeerChanges { get; }
    Task InitializeAsync();
    Task RequestPairingAsync(PeerEndpoint endpoint, string deviceId, string? displayName);
    Task ConfirmPairingAsync(string deviceId, string? displayName, string pin);
    Task UnpairAsync(string deviceId);
    Task<ISynchronizationPeer> GetPeerAsync(PeerEndpoint endpoint);
}
