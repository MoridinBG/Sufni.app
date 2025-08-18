using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using ServiceDiscovery;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class PairingClientViewModel : ViewModelBase
{
    private const string DeviceIdKey = "DeviceId";
    
    #region Observable properties

    [ObservableProperty] private string? serverUrl;
    [ObservableProperty] private string? deviceId;
    [ObservableProperty] private string? pin;
    [ObservableProperty] private bool isRequestSent;
    [ObservableProperty] private bool isPaired;

    #endregion

    #region Private members

    private ISecureStorage? secureStorage;
    private IHttpApiService? httpApiService;
    private IServiceDiscovery? serviceDiscovery;

    #endregion Private members

    #region Constructors

    public PairingClientViewModel()
    {
        _ = InitAsync();
    }

    #endregion

    #region Private methods

    private async Task InitAsync()
    {
        secureStorage = App.Current?.Services?.GetService<ISecureStorage>();
        httpApiService = App.Current?.Services?.GetService<IHttpApiService>();
        serviceDiscovery = App.Current?.Services?.GetKeyedService<IServiceDiscovery>("sync");

        Debug.Assert(httpApiService is not null);
        Debug.Assert(secureStorage is not null);
        Debug.Assert(serviceDiscovery is not null);

        DeviceId = await secureStorage.GetStringAsync(DeviceIdKey);
        if (DeviceId is null)
        {
            DeviceId = Guid.NewGuid().ToString();
            await secureStorage.SetStringAsync(DeviceIdKey, DeviceId);
        }

        IsPaired = await httpApiService.IsPairedAsync();

        serviceDiscovery.ServiceAdded += (_, e) =>
        {
            ServerUrl = $"https://{e.Announcement.Address.ToString()}:{e.Announcement.Port}";
        };
        serviceDiscovery.ServiceRemoved += (_, _) => ServerUrl = null;
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RequestPairing()
    {
        Debug.Assert(httpApiService is not null);
        Debug.Assert(ServerUrl is not null);
        Debug.Assert(DeviceId is not null);

        await httpApiService.RequestPairingAsync(ServerUrl, DeviceId);
        IsRequestSent =  true;
    }

    [RelayCommand]
    private async Task ConfirmPairing()
    {
        Debug.Assert(httpApiService is not null);
        Debug.Assert(secureStorage is not null);
        Debug.Assert(ServerUrl is not null);
        Debug.Assert(Pin is not null);
        Debug.Assert(DeviceId is not null);

        try
        {
            await httpApiService.ConfirmPairingAsync(DeviceId, Pin);
            IsPaired = true;
            Pin = null;
            IsRequestSent = false;
            ErrorMessages.Clear();
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not pair: {e.Message}");
        }

        OpenPreviousPage();
    }

    [RelayCommand]
    private async Task Unpair()
    {
        Debug.Assert(httpApiService is not null);
        Debug.Assert(secureStorage is not null);
        Debug.Assert(DeviceId is not null);

        try
        {
            IsPaired = false;
            await httpApiService.UnpairAsync(DeviceId);
        }
        catch (HttpRequestException e)
        {
            Notifications.Add($"Could unpair only locally: {e.Message}");
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not unpair: {e.Message}");
        }
    }

    [RelayCommand]
    private void Loaded()
    {
        Debug.Assert(serviceDiscovery is not null);
        serviceDiscovery.StartBrowse(SynchronizationServerService.ServiceType);
    }

    [RelayCommand]
    private void Unloaded()
    {
        Debug.Assert(serviceDiscovery is not null);
        serviceDiscovery.StopBrowse();
    }

    #endregion Commands
}
