using NSubstitute;

using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class PairingServerCoordinatorTests
{
    private readonly ISynchronizationServerService server = Substitute.For<ISynchronizationServerService>();

    private PairingServerCoordinator CreateCoordinator() => new(server);

    [Fact]
    public async Task StartServerAsync_DelegatesToSynchronizationServer()
    {
        var coordinator = CreateCoordinator();

        await coordinator.StartServerAsync();

        await server.Received(1).StartAsync();
    }

    [Fact]
    public void PairingRequested_IsReRaisedFromServerEvent()
    {
        var coordinator = CreateCoordinator();
        PairingRequestedEventArgs? captured = null;
        coordinator.PairingRequested += (_, e) => captured = e;

        var args = new PairingRequestedEventArgs("device-xyz", "My Phone", "1234");
        server.PairingRequested += Raise.EventWith(server, args);

        Assert.Same(args, captured);
    }

    [Fact]
    public void PairingConfirmed_IsReRaisedFromServerEvent()
    {
        var coordinator = CreateCoordinator();
        PairingEventArgs? captured = null;
        coordinator.PairingConfirmed += (_, e) => captured = e;

        var device = new PairedDevice("device-xyz", "My Phone", DateTime.UtcNow);
        var args = new PairingEventArgs(device);
        server.PairingConfirmed += Raise.EventWith(server, args);

        Assert.Same(args, captured);
    }
}
