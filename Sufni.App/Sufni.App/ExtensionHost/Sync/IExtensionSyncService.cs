using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services;

namespace Sufni.App.ExtensionHost.Sync;

public interface IExtensionSyncService
{
    Task<List<ExtensionSyncEnvelope>> CreateBatchesAsync(long since, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyBatchesAsync(
        IEnumerable<ExtensionSyncEnvelope> envelopes,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default);
}

