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
    [ObservableProperty] private double remaining;

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
            Remaining -= 1.0 / SynchronizationServerService.PinTtlSeconds;
            if (!(Remaining <= 0)) return;
            PairingPin = null;
            timer.Stop();
        };

        coordinator.PairingRequested += (_, e) =>
        {
            PairingPin = e.Pin;
            RequestingId = e.DeviceId;
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
