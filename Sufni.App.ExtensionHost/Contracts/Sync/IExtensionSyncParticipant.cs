using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.Sync;

public interface IExtensionSyncParticipant
{
    string ExtensionId { get; }
    Task<ExtensionSyncEnvelope?> CreateBatchAsync(
        long sinceExclusive,
        long upperInclusive,
        CancellationToken cancellationToken);
    Task<ExtensionSyncPrepareResult> PrepareBatchAsync(
        ExtensionSyncEnvelope envelope,
        CancellationToken cancellationToken);

    Task<ExtensionSyncApplyResult> ApplyPreparedBatchAsync(
        IExtensionSyncPreparedBatch batch,
        CancellationToken cancellationToken);
}

