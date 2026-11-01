using System.Threading.Tasks;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Coordinators;

public enum InboundSyncWriteResult { Applied, Failed }

/// <summary>Owns desktop inbound persistence and publication before the peer is acknowledged.</summary>
public interface IInboundSyncCoordinator
{
    Task<InboundSyncWriteResult> ApplyPushAsync(SynchronizationData data);
    Task<SynchronizationData> GetPullAsync(long sinceExclusive);
}
