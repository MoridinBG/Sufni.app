using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class PairingViewModel : ViewModelBase
{
    private readonly System.Timers.Timer timer = new(1000);

    #region Observable properties

    [ObservableProperty] private string? pairingPin;
    [ObservableProperty] private string? requestingId;
    [ObservableProperty] private double remaining;

    #endregion

    #region Constructors

    public PairingViewModel()
    {
        timer.Elapsed += (_, _) =>
        {
            Remaining -= 1.0 / SynchronizationServerService.PinTtlSeconds;
            if (!(Remaining <= 0)) return;
            PairingPin = null;
            timer.Stop();
        };

        Debug.Assert(App.Current is not null);
        var synchronizationServer = App.Current.Services?.GetService<ISynchronizationServerService>();

        Debug.Assert(synchronizationServer is not null);

        synchronizationServer.PairingPinCallback = (id, pin) =>
        {
            PairingPin = pin;
            RequestingId = id;
            Remaining = 0.999;
            timer.Start();
        };

        if (!Design.IsDesignMode)
        {
            await synchronizationServer.StartAsync();
        }
    }

    #endregion Constructors
}