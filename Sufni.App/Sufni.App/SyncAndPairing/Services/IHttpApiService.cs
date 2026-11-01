using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Services;

// Client-side HTTP contract for pairing, entity sync, and session blob/source
// transfer. Higher-level sync code should not depend on endpoint details.
public interface IHttpApiService
{
    public string? ServerUrl { get; set; }
    public IObservable<bool> PairedState { get; }
    public Task RequestPairingAsync(string url, string deviceId, string? displayName);
    public Task ConfirmPairingAsync(string deviceId, string? displayName, string pin);
    public Task UnpairAsync(string deviceId);
    public Task<bool> IsPairedAsync();
    public Task<PeerIdentity> GetPeerIdentityAsync();
    public Task<SynchronizationData> PullSyncAsync(long since = 0, CancellationToken cancellationToken = default);
    public Task PushSyncAsync(SynchronizationData syncData, CancellationToken cancellationToken = default);
    public Task<List<Guid>> GetIncompleteSessionIdsAsync(CancellationToken cancellationToken = default);
    public Task<SynchronizationTransferResult<SessionBlobPayload>> GetSessionPsstAsync(Guid id, CancellationToken cancellationToken = default);
    public Task<SynchronizationTransferStatus> PatchSessionPsstAsync(Guid id, byte[] data, string? fingerprint, CancellationToken cancellationToken = default);
    public Task<List<Guid>> GetIncompleteSessionSourceIdsAsync(CancellationToken cancellationToken = default);
    public Task<SynchronizationTransferResult<RecordedSessionSourcePayload>> GetRecordedSessionSourceAsync(Guid id, CancellationToken cancellationToken = default);
    public Task<SynchronizationTransferStatus> PatchRecordedSessionSourceAsync(RecordedSessionSourcePayload source, CancellationToken cancellationToken = default);
}
