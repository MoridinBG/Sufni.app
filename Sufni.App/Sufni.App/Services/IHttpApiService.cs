using System;
using Sufni.App.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.Services;

// Client-side HTTP contract for pairing, entity sync, and session blob/source
// transfer. Higher-level sync code should not depend on endpoint details.
public interface IHttpApiService
{
    public string? ServerUrl { get; set; }
    public Task RequestPairingAsync(string url, string deviceId, string? displayName);
    public Task ConfirmPairingAsync(string deviceId, string? displayName, string pin);
    public Task UnpairAsync(string deviceId);
    public Task<bool> IsPairedAsync();
    public Task<SynchronizationData> PullSyncAsync(long since = 0);
    public Task PushSyncAsync(SynchronizationData syncData);
    public Task<List<Guid>> GetIncompleteSessionIdsAsync();
    public Task<byte[]?> GetSessionPsstAsync(Guid id);
    public Task PatchSessionPsstAsync(Guid id, byte[] data);
    public Task<List<Guid>> GetIncompleteSessionSourceIdsAsync();
    public Task<RecordedSessionSourceTransfer?> GetRecordedSessionSourceAsync(Guid id);
    public Task PatchRecordedSessionSourceAsync(RecordedSessionSourceTransfer source);
}
