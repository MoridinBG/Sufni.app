using NSubstitute;

using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.ViewModels;
using Sufni.App.Tests.TestSupport.Async;
namespace Sufni.App.Tests.SyncAndPairing.ViewModels;

public class PairingServerViewModelTests
{
    [Fact]
    public async Task PairingRequested_HidesPinAfterThirtySeconds()
    {
        var coordinator = Substitute.For<IPairingServerCoordinator>();
        var time = new SignalingFakeTimeProvider();
        var viewModel = new PairingServerViewModel(coordinator, new InlineUiThreadDispatcher(), time);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.PairingPin) && viewModel.PairingPin is null)
                expired.TrySetResult();
        };
        coordinator.PairingRequested += Raise.EventWith(
            coordinator, new PairingRequestedEventArgs("device-1", "Phone", "123456"));

        for (var second = 0; second < 30; second++)
        {
            await time.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("123456", viewModel.PairingPin);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        await expired.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Null(viewModel.PairingPin);
    }

    [Fact]
    public void PairingRequested_AppliesPromptStateThroughUiDispatcher()
    {
        var coordinator = Substitute.For<IPairingServerCoordinator>();
        var dispatcher = new RecordingUiThreadDispatcher();
        var viewModel = new PairingServerViewModel(coordinator, dispatcher, new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

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
        var viewModel = new PairingServerViewModel(coordinator, dispatcher, new Microsoft.Extensions.Time.Testing.FakeTimeProvider())
        {
            PairingPin = "123456"
        };
        var device = new PairedDevice("device-1", "Phone", DateTime.UtcNow.AddDays(1));

        coordinator.PairingConfirmed += Raise.EventWith(coordinator, new PairingEventArgs(device));

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Null(viewModel.PairingPin);
    }
}
