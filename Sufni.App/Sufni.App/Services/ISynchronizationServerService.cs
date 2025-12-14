using System;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface ISynchronizationServerService
{
    public Task StartAsync();
    public Action<string, string>? PairingRequested { get; set; }
    public Action<SynchronizationData>? SynchronizationDataArrived { get; set; }
    public Action<Guid>? SessionDataArrived { get; set; }
    public event EventHandler? PairingConfirmed;
    public event EventHandler? Unpaired;
}