using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class PairingViewModel : TabPageViewModelBase
{
    private readonly System.Timers.Timer timer = new(1000);
    
    #region Observable properties

    [ObservableProperty] private string? pairingPin;
    [ObservableProperty] private string? requestingId;
    [ObservableProperty] private double remaining;
    public ObservableCollection<PairedDevice> PairedDevices { get; } = [];

    #endregion
    
    #region Constructors

    public PairingViewModel()
    {
        Name = "Pairing";

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

        _ = LoadPairedDevicesAsync();
    }

    #endregion Constructors

    #region Private methods

    private async Task LoadPairedDevicesAsync()
    {
        var databaseService = App.Current?.Services?.GetService<IDatabaseService>();
        Debug.Assert(databaseService != null, nameof(databaseService) + " != null");

        try
        {
            var pairedDevices = await databaseService.GetPairedDevicesAsync();
            foreach (var device in pairedDevices)
            {
                PairedDevices.Add(device);
            }
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not load paired devices: {e.Message}");
        }
    }

    #endregion
}