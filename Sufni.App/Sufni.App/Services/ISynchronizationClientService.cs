using System;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface ISynchronizationClientService
{
    public Task SyncAll(IProgress<SynchronizationProgressSnapshot>? progress = null);
}
