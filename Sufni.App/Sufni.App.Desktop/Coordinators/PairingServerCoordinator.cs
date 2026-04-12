using System;
using System.Threading.Tasks;
using Sufni.App.Services;
using Serilog;

namespace Sufni.App.Coordinators;

/// <summary>
/// Desktop-only singleton that re-exposes the synchronization
/// server's pairing events as plain .NET events the
/// <see cref="ViewModels.PairingServerViewModel"/> can subscribe to
/// without having a direct
/// <see cref="ISynchronizationServerService"/> reference.
/// </summary>
public sealed class PairingServerCoordinator : IPairingServerCoordinator
{
    private static readonly ILogger logger = Log.ForContext<PairingServerCoordinator>();

    private readonly ISynchronizationServerService synchronizationServer;

    public event EventHandler<PairingRequestedEventArgs>? PairingRequested;
    public event EventHandler<PairingEventArgs>? PairingConfirmed;

    public PairingServerCoordinator(ISynchronizationServerService synchronizationServer)
    {
        this.synchronizationServer = synchronizationServer;

        synchronizationServer.PairingRequested += OnPairingRequested;
        synchronizationServer.PairingConfirmed += OnPairingConfirmed;
    }

    public Task StartServerAsync()
    {
        return synchronizationServer.StartAsync();
    }

    private void OnPairingRequested(object? sender, PairingRequestedEventArgs e)
    {
        logger.Verbose("Received pairing request from {DeviceId}", e.DeviceId);
        PairingRequested?.Invoke(this, e);
    }

    private void OnPairingConfirmed(object? sender, PairingEventArgs e)
    {
        logger.Verbose("Received pairing confirmation for {DeviceId}", e.Device.DeviceId);
        PairingConfirmed?.Invoke(this, e);
    }
}
