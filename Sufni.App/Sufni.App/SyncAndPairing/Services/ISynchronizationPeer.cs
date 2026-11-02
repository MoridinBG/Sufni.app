using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Services;

/// <summary>A captured transport bound to one peer; retirement cancels its runs.</summary>
public interface ISynchronizationPeer
{
    CancellationToken Lifetime { get; }
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
