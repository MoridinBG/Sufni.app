using System;
using System.Threading.Tasks;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Services;

/// <summary>Pairing and discovery binding for peer-scoped HTTP transports.</summary>
public interface IHttpApiService
{
    IObservable<bool> PairedState { get; }
    Task RequestPairingAsync(PeerEndpoint endpoint, string deviceId, string? displayName);
    Task ConfirmPairingAsync(string deviceId, string? displayName, string pin);
    Task UnpairAsync(string deviceId);
    Task<bool> IsPairedAsync();
    Task<ISynchronizationPeer> GetPeerAsync(PeerEndpoint endpoint);
}
