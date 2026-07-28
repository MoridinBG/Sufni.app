using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Sync;

using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Extensibility.Sync;

internal sealed record ExtensionSyncApplyPlan(
    IReadOnlyList<PreparedExtensionSyncBatch> Batches);

internal sealed record PreparedExtensionSyncBatch(
    IExtensionSyncParticipant Participant,
    IExtensionSyncPreparedBatch Batch);

internal interface IExtensionSyncService
{
    Task<List<ExtensionSyncEnvelope>> CreateBatchesAsync(
        long sinceExclusive,
        long upperInclusive,
        CancellationToken cancellationToken = default);

    Task<ExtensionSyncApplyPlan> PrepareBatchesAsync(
        IEnumerable<ExtensionSyncEnvelope> envelopes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyPreparedBatchesAsync(
        ExtensionSyncApplyPlan plan,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyBatchesAsync(
        IEnumerable<ExtensionSyncEnvelope> envelopes,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default);
}
