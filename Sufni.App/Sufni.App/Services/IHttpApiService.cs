using System;
using Sufni.App.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sufni.App.Services;

internal interface IHttpApiService
{
    public Task RequestPairingAsync(string url, string deviceId);
    public Task ConfirmPairingAsync(string deviceId, string pin);
    public Task UnpairAsync();
    public Task<bool> IsPairedAsync();
    public Task<SynchronizationData> PullSyncAsync(long since = 0);
    public Task PushSyncAsync(SynchronizationData syncData);
    public Task<List<Guid>> GetIncompleteSessionIdsAsync();
    public Task<byte[]?> GetSessionPsstAsync(Guid id);
    public Task PatchSessionPsstAsync(Guid id, byte[] data);
}
