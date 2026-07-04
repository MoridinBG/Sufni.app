using System;
using System.Threading.Tasks;

namespace Sufni.App.SyncAndPairing.Services;

public interface ISynchronizationClientService
{
    public Task<SynchronizationRunResult> SyncAll(IProgress<SynchronizationProgressSnapshot>? progress = null);
}
