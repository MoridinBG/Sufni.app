using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class PairingServerViewModel : ViewModelBase
{
    private readonly IPairingServerCoordinator coordinator;
    private CancellationTokenSource? pairingCountdownCancellation;

    #region Observable properties

    [ObservableProperty] private string? pairingPin;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestingName))]
    private string? requestingId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestingName))]
    private string? requestingDisplayName;
    [ObservableProperty] private double remaining;

    /// <summary>
    /// Human-readable label for the pairing prompt: prefer the
    /// requester's <see cref="RequestingDisplayName"/>, fall back to
    /// the opaque <see cref="RequestingId"/> when no display name was
    /// supplied (e.g. older clients) or it is blank/whitespace.
    /// </summary>
    public string? RequestingName =>
        string.IsNullOrWhiteSpace(RequestingDisplayName) ? RequestingId : RequestingDisplayName;

    #endregion

    #region Constructors

    public PairingServerViewModel(
        IPairingServerCoordinator coordinator,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        this.coordinator = coordinator;

        // The desktop view's LoadedCommand is bound to AttachedToVisualTree
        // which can fire more than once per VM lifetime. This VM is a
        // desktop singleton, so the constructor runs exactly once and is
        // the right level for these subscriptions and the timer hookup.
        coordinator.PairingRequested += (_, e) =>
        {
            UiThreadDispatcher.Post(() =>
                StartPairingRequest(e.Pin, e.DeviceId, e.DisplayName));
        };

        // Hide the panel displaying the PIN when the pairing is done.
        coordinator.PairingConfirmed += (_, _) =>
            UiThreadDispatcher.Post(ClearPairingRequest);
    }

    #endregion Constructors

    #region Commands

    [RelayCommand]
    private async Task Loaded()
    {
        if (!Design.IsDesignMode)
        {
            await coordinator.StartServerAsync();
        }
    }

    #endregion Commands

    #region Private methods

    private void StartPairingRequest(string pin, string deviceId, string? displayName)
    {
        PairingPin = pin;
        RequestingId = deviceId;
        RequestingDisplayName = displayName;
        Remaining = 0.999;
        RestartPairingCountdown();
    }

    private void ClearPairingRequest()
    {
        PairingPin = null;
        CancelPairingCountdown();
    }

    private void RestartPairingCountdown()
    {
        CancelPairingCountdown();
        pairingCountdownCancellation = new CancellationTokenSource();
        _ = RunPairingCountdownAsync(pairingCountdownCancellation.Token);
    }

    private void CancelPairingCountdown()
    {
        pairingCountdownCancellation?.Cancel();
        pairingCountdownCancellation?.Dispose();
        pairingCountdownCancellation = null;
    }

    private async Task RunPairingCountdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(1000, cancellationToken);
                await UiThreadDispatcher.InvokeAsync(() =>
                {
                    Remaining -= 1.0 / SynchronizationProtocol.PinTtlSeconds;
                    if (Remaining <= 0)
                    {
                        ClearPairingRequest();
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer PIN or a successful pairing.
        }
    }

    #endregion Private methods
}
