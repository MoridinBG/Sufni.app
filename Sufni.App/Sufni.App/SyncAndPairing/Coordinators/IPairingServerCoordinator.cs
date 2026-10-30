using System;
using System.Threading.Tasks;

using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>Server pairing events and the launch entry point used by the shell.</summary>
public interface IPairingServerCoordinator
{
    Task StartServerAsync();

    event EventHandler<PairingRequestedEventArgs>? PairingRequested;
    event EventHandler<PairingEventArgs>? PairingConfirmed;
}
