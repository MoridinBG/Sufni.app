using System;
using System.Threading.Tasks;

using Sufni.App.Sessions.Services;
namespace Sufni.App.SyncAndPairing.Services;

public interface ISynchronizationServerService
{
    public Task<ServerStartResult> StartAsync();
    public Task StopAsync();
    public event EventHandler<PairingRequestedEventArgs>? PairingRequested;
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityStarted;
    public event EventHandler<SynchronizationActivityEventArgs>? SyncActivityEnded;
    public event EventHandler<PairingEventArgs>? PairingConfirmed;
    public event EventHandler<PairingEventArgs>? Unpaired;
}
