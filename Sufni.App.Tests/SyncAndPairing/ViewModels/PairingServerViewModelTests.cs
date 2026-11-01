using System.Reactive.Subjects;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.ViewModels;

namespace Sufni.App.Tests.SyncAndPairing.ViewModels;

public class PairingServerViewModelTests
{
    [Fact]
    public void PromptCountdown_UsesOwnersExpiryInsteadOfStartingAnotherThirtySeconds()
    {
        var time = new FakeTimeProvider();
        using var prompt = new BehaviorSubject<PairingChallenge?>(new("device-1", "Phone", "123456", time.GetUtcNow().AddSeconds(15)));
        var coordinator = Substitute.For<IInboundSyncCoordinator>();
        coordinator.PairingPrompt.Returns(prompt);
        using var timers = new ManualUiTimerScheduler();
        using var viewModel = new PairingServerViewModel(coordinator, new InlineUiThreadDispatcher(), time, timers);
        Assert.Equal(0.5, viewModel.Remaining);

        time.Advance(TimeSpan.FromSeconds(15));
        timers.FireAll();

        Assert.Null(viewModel.PairingPin);
        Assert.Equal(0, viewModel.Remaining);
    }

    [Fact]
    public async Task PromptAndConfirmation_PublishVisibleStateThroughDispatcher()
    {
        var time = new FakeTimeProvider();
        using var prompt = new Subject<PairingChallenge?>();
        var coordinator = Substitute.For<IInboundSyncCoordinator>();
        coordinator.PairingPrompt.Returns(prompt);
        var dispatcher = new ManualUiThreadDispatcher();
        using var timers = new ManualUiTimerScheduler();
        using var viewModel = new PairingServerViewModel(coordinator, dispatcher, time, timers);

        prompt.OnNext(new("device-1", "Phone", "123456", time.GetUtcNow().AddSeconds(30)));
        Assert.Null(viewModel.PairingPin);
        await dispatcher.DrainAsync();
        Assert.Equal("123456", viewModel.PairingPin);
        Assert.Equal("Phone", viewModel.RequestingName);
        Assert.Equal(0.999, viewModel.Remaining);

        prompt.OnNext(null);
        await dispatcher.DrainAsync();
        Assert.Null(viewModel.PairingPin);
    }

    [Fact]
    public void NewPrompt_UsesItsOwnExpiry_AndDisposalStopsObservation()
    {
        var time = new FakeTimeProvider();
        using var prompt = new Subject<PairingChallenge?>();
        var coordinator = Substitute.For<IInboundSyncCoordinator>();
        coordinator.PairingPrompt.Returns(prompt);
        using var timers = new ManualUiTimerScheduler();
        using var viewModel = new PairingServerViewModel(coordinator, new InlineUiThreadDispatcher(), time, timers);
        prompt.OnNext(new("first", null, "123456", time.GetUtcNow().AddSeconds(30)));
        time.Advance(TimeSpan.FromSeconds(20));
        prompt.OnNext(new("second", null, "654321", time.GetUtcNow().AddSeconds(30)));
        time.Advance(TimeSpan.FromSeconds(10));
        timers.FireAll();
        Assert.Equal("654321", viewModel.PairingPin);
        Assert.Equal(2.0 / 3, viewModel.Remaining, 5);

        viewModel.Dispose();
        prompt.OnNext(null);
        Assert.Equal("654321", viewModel.PairingPin);
    }
}
