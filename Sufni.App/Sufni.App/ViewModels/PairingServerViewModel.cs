using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class PairingServerViewModel : ViewModelBase
{
    private readonly System.Timers.Timer timer = new(1000);
    private readonly ISynchronizationServerService synchronizationServer;

    #region Observable properties

    [ObservableProperty] private string? pairingPin;
    [ObservableProperty] private string? requestingId;
    [ObservableProperty] private double remaining;

    #endregion

    #region Constructors

    public PairingServerViewModel()
    {
        synchronizationServer = null!;
    }

    public PairingServerViewModel(ISynchronizationServerService synchronizationServer)
    {
        this.synchronizationServer = synchronizationServer;
    }

    #endregion Constructors

    #region Commands

    [RelayCommand]
    private async Task Loaded()
    {
        timer.Elapsed += (_, _) =>
        {
            Remaining -= 1.0 / SynchronizationServerService.PinTtlSeconds;
            if (!(Remaining <= 0)) return;
            PairingPin = null;
            timer.Stop();
        };

        synchronizationServer.PairingRequested = (id, pin) =>
        {
            PairingPin = pin;
            RequestingId = id;
            Remaining = 0.999;
            timer.Start();
        };

        // Hide the panel displaying the PIN when the pairing is done.
        synchronizationServer.PairingConfirmed += (_, _) => PairingPin = null;

        if (!Design.IsDesignMode)
        {
            await synchronizationServer.StartAsync();
        }
    }

    #endregion Commands
}