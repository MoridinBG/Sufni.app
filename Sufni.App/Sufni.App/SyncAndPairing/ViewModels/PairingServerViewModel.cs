using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Infrastructure;

using Sufni.App.Shared.Base;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.SyncAndPairing.ViewModels;

public partial class PairingServerViewModel : ViewModelBase
{
    private CancellationTokenSource? pairingCountdownCancellation;

    #region Observable properties

    [ObservableProperty] public partial string? PairingPin { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestingName))]
    public partial string? RequestingId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestingName))]
    public partial string? RequestingDisplayName { get; set; }
    [ObservableProperty] public partial double Remaining { get; set; }

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
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
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
                await Task.Delay(TimeSpan.FromSeconds(1), Clock, cancellationToken);
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
