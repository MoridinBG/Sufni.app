using System;
using System.Threading.Tasks;

namespace Sufni.App.SyncAndPairing.Services;

public interface ISynchronizationServerService
{
    public Task<ServerStartResult> StartAsync();
    public Task StopAsync();
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityStarted;
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityEnded;
}
