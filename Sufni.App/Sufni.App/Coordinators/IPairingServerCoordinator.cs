using System;
using System.Threading.Tasks;
using Sufni.App.Services;

namespace Sufni.App.Coordinators;

/// <summary>
/// Desktop-only thin coordinator that owns the
/// <see cref="ISynchronizationServerService"/> pairing-event
/// subscriptions and re-exposes them as plain events the
/// <see cref="ViewModels.PairingServerViewModel"/> consumes. Also
/// provides a passthrough for <see cref="StartServerAsync"/> so the
/// view model has zero direct
/// <see cref="ISynchronizationServerService"/> references.
/// </summary>
public interface IPairingServerCoordinator
{
    /// <summary>
    /// Starts the embedded sync server. The VM still drives this from
    /// its <c>Loaded</c> command, but routes through the coordinator
    /// so the VM has zero direct <c>ISynchronizationServerService</c>
    /// references.
    /// </summary>
    Task StartServerAsync();

    event EventHandler<PairingRequestedEventArgs>? PairingRequested;
    event EventHandler<PairingEventArgs>? PairingConfirmed;
}
