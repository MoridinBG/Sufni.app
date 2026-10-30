using System;
using System.Threading.Tasks;
using System.Threading;

namespace Sufni.App.SyncAndPairing.Services;

public interface ISynchronizationClientService
{
    public Task<SynchronizationRunResult> SyncAll(IProgress<SynchronizationProgressSnapshot>? progress = null, CancellationToken cancellationToken = default);
}
