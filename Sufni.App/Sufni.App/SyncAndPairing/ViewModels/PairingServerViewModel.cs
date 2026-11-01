using System;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.SyncAndPairing.ViewModels;

public partial class PairingServerViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable subscription;
    private readonly IUiTimerScheduler timers;
    private IDisposable? countdown;

    [ObservableProperty] public partial string? PairingPin { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestingName))]
    public partial string? RequestingId { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestingName))]
    public partial string? RequestingDisplayName { get; set; }
    [ObservableProperty] public partial double Remaining { get; set; }

    public string? RequestingName => string.IsNullOrWhiteSpace(RequestingDisplayName) ? RequestingId : RequestingDisplayName;

    public PairingServerViewModel(IInboundSyncCoordinator coordinator, IUiThreadDispatcher dispatcher,
        TimeProvider clock, IUiTimerScheduler timers) : base(dispatcher, clock)
    {
        this.timers = timers;
        subscription = coordinator.PairingPrompt.Subscribe(challenge => UiThreadDispatcher.Post(() => Show(challenge)));
    }

    private void Show(PairingChallenge? challenge)
    {
        countdown?.Dispose();
        countdown = null;
        PairingPin = challenge?.Pin;
        RequestingId = challenge?.DeviceId;
        RequestingDisplayName = challenge?.DisplayName;
        if (challenge is null) { Remaining = 0; return; }
        UpdateRemaining(challenge.ExpiresAt);
        if (PairingPin is not null)
            countdown = timers.SchedulePeriodic(TimeSpan.FromSeconds(1), () => UpdateRemaining(challenge.ExpiresAt));
    }

    private void UpdateRemaining(DateTimeOffset expiresAt)
    {
        Remaining = Math.Clamp((expiresAt - Clock.GetUtcNow()).TotalSeconds / SynchronizationProtocol.PinTtlSeconds, 0, 0.999);
        if (Remaining > 0) return;
        PairingPin = null;
        countdown?.Dispose();
        countdown = null;
    }

    public void Dispose()
    {
        subscription.Dispose();
        countdown?.Dispose();
    }
}
