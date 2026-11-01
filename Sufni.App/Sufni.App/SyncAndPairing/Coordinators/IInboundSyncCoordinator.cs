using System;
using System.Threading.Tasks;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Coordinators;

public enum InboundSyncWriteResult { Applied, Failed }
public enum InboundSourceApplyResult { Applied, InvalidPayload, Failed }

/// <summary>Owns desktop inbound persistence and publication before the peer is acknowledged.</summary>
public interface IInboundSyncCoordinator
{
    Task<InboundSyncWriteResult> ApplyPushAsync(SynchronizationData data);
    Task<SynchronizationData> GetPullAsync(long sinceExclusive);
    Task<SessionDataApplyResult> ApplySessionBlobAsync(Guid id, SessionBlobPayload payload);
    Task<InboundSourceApplyResult> ApplySourceAsync(RecordedSessionSourcePayload payload);
}
