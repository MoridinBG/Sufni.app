
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Tests.TestSupport.Doubles;

#pragma warning disable CS0067
internal sealed class TestSynchronizationServerService : ISynchronizationServerService
{
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityStarted;
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityEnded;

    public Task<ServerStartResult> StartAsync() => Task.FromResult<ServerStartResult>(new ServerStartResult.Started());
    public Task StopAsync() => Task.CompletedTask;

    public void RaiseSyncActivityStarted(SynchronizationProgressSnapshot progress)
    {
        SyncActivityStarted?.Invoke(this, new SynchronizationActivityEventArgs(progress));
    }

    public void RaiseSyncActivityEnded(SynchronizationProgressSnapshot progress)
    {
        SyncActivityEnded?.Invoke(this, new SynchronizationActivityEventArgs(progress));
    }
}
#pragma warning restore CS0067
