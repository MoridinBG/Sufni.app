using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.ViewModels;

public class PairingServerViewModelTests
{
    [Fact]
    public void PairingRequested_AppliesPromptStateThroughUiDispatcher()
    {
        var coordinator = Substitute.For<IPairingServerCoordinator>();
        var dispatcher = new RecordingUiThreadDispatcher();
        var viewModel = new PairingServerViewModel(coordinator, dispatcher);

        coordinator.PairingRequested += Raise.EventWith(
            coordinator,
            new PairingRequestedEventArgs("device-1", "Phone", "123456"));

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Equal("123456", viewModel.PairingPin);
        Assert.Equal("device-1", viewModel.RequestingId);
        Assert.Equal("Phone", viewModel.RequestingDisplayName);
        Assert.Equal("Phone", viewModel.RequestingName);
        Assert.Equal(0.999, viewModel.Remaining);
    }

    [Fact]
    public void PairingConfirmed_ClearsPromptThroughUiDispatcher()
    {
        var coordinator = Substitute.For<IPairingServerCoordinator>();
        var dispatcher = new RecordingUiThreadDispatcher();
        var viewModel = new PairingServerViewModel(coordinator, dispatcher)
        {
            PairingPin = "123456"
        };
        var device = new PairedDevice("device-1", "Phone", DateTime.UtcNow.AddDays(1));

        coordinator.PairingConfirmed += Raise.EventWith(coordinator, new PairingEventArgs(device));

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Null(viewModel.PairingPin);
    }
}
