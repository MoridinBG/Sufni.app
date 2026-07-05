using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    public Task<SynchronizationData> PullSyncAsync(long since = 0);
    public Task PushSyncAsync(SynchronizationData syncData);
    public Task<List<Guid>> GetIncompleteSessionIdsAsync();
    public Task<SessionBlobPayload?> GetSessionPsstAsync(Guid id);
    public Task PatchSessionPsstAsync(Guid id, byte[] data, string? fingerprint);
    public Task<List<Guid>> GetIncompleteSessionSourceIdsAsync();
    public Task<RecordedSessionSourcePayload?> GetRecordedSessionSourceAsync(Guid id);
    public Task PatchRecordedSessionSourceAsync(RecordedSessionSourcePayload source);
}
