using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class PairingServerViewModel : ViewModelBase
{
    private readonly System.Timers.Timer timer = new(1000);
    private readonly IPairingServerCoordinator coordinator;

    #region Observable properties

    [ObservableProperty] private string? pairingPin;
    [ObservableProperty] private string? requestingId;
    [ObservableProperty] private string? requestingDisplayName;
    [ObservableProperty] private double remaining;

    /// <summary>
    /// Human-readable label for the pairing prompt: prefer the
    /// requester's <see cref="RequestingDisplayName"/>, fall back to
    /// the opaque <see cref="RequestingId"/> when no display name was
    /// supplied (e.g. older clients) or it is blank/whitespace.
    /// </summary>
    public string? RequestingName =>
        string.IsNullOrWhiteSpace(RequestingDisplayName) ? RequestingId : RequestingDisplayName;

    partial void OnRequestingIdChanged(string? value) => OnPropertyChanged(nameof(RequestingName));
    partial void OnRequestingDisplayNameChanged(string? value) => OnPropertyChanged(nameof(RequestingName));

    #endregion

    #region Constructors

    public PairingServerViewModel()
    {
        coordinator = null!;
    }

    public PairingServerViewModel(IPairingServerCoordinator coordinator)
    {
        this.coordinator = coordinator;

        // The desktop view's LoadedCommand is bound to AttachedToVisualTree
        // which can fire more than once per VM lifetime. This VM is a
        // desktop singleton, so the constructor runs exactly once and is
        // the right level for these subscriptions and the timer hookup.
        timer.Elapsed += (_, _) =>
        {
            Remaining -= 1.0 / SynchronizationProtocol.PinTtlSeconds;
            if (!(Remaining <= 0)) return;
            PairingPin = null;
            timer.Stop();
        };

        coordinator.PairingRequested += (_, e) =>
        {
            PairingPin = e.Pin;
            RequestingId = e.DeviceId;
            RequestingDisplayName = e.DisplayName;
            Remaining = 0.999;
            timer.Start();
        };

        // Hide the panel displaying the PIN when the pairing is done.
        coordinator.PairingConfirmed += (_, _) => PairingPin = null;
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
}
