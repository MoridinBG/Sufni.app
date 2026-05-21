using Sufni.App.Services;

namespace Sufni.App.Tests.Infrastructure;

#pragma warning disable CS0067
internal sealed class TestSynchronizationServerService : ISynchronizationServerService
{
    public event EventHandler<PairingRequestedEventArgs>? PairingRequested;
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityStarted;
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityEnded;
    public event EventHandler<SynchronizationDataArrivedEventArgs>? SynchronizationDataArrived;
    public event EventHandler<SessionDataArrivedEventArgs>? SessionDataArrived;
    public event EventHandler<SessionDataArrivedEventArgs>? SessionSourceDataArrived;
    public event EventHandler<PairingEventArgs>? PairingConfirmed;
    public event EventHandler<PairingEventArgs>? Unpaired;

    public Task StartAsync() => Task.CompletedTask;

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
