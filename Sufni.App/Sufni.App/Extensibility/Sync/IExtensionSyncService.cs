using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Sync;

using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Extensibility.Sync;

internal interface IExtensionSyncService
{
    Task<List<ExtensionSyncEnvelope>> CreateBatchesAsync(long since, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyBatchesAsync(
        IEnumerable<ExtensionSyncEnvelope> envelopes,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default);
}
