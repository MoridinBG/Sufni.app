using System;
using System.Threading.Tasks;

namespace Sufni.App.Services;

public interface ISynchronizationServerService
{
    public Task StartAsync();
    public event EventHandler<PairingRequestedEventArgs>? PairingRequested;
    public event EventHandler<SynchronizationDataArrivedEventArgs>? SynchronizationDataArrived;
    public event EventHandler<SessionDataArrivedEventArgs>? SessionDataArrived;
    public event EventHandler<SessionDataArrivedEventArgs>? SessionSourceDataArrived;
    public event EventHandler<PairingEventArgs>? PairingConfirmed;
    public event EventHandler<PairingEventArgs>? Unpaired;
}
