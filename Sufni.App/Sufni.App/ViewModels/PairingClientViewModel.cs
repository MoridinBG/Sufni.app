using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FriendlyNameProvider;
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

    private readonly ISecureStorage secureStorage;
    private readonly IHttpApiService httpApiService;
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IFriendlyNameProvider friendlyNameProvider;
    private readonly INavigator navigator;

    #endregion Private members

    #region Constructors

    public PairingClientViewModel()
    {
        secureStorage = null!;
        httpApiService = null!;
        serviceDiscovery = null!;
        friendlyNameProvider = null!;
        navigator = null!;
    }

    public PairingClientViewModel(
        ISecureStorage secureStorage,
        IHttpApiService httpApiService,
        [FromKeyedServices("sync")] IServiceDiscovery serviceDiscovery,
        IFriendlyNameProvider friendlyNameProvider,
        INavigator navigator)
    {
        this.secureStorage = secureStorage;
        this.httpApiService = httpApiService;
        this.serviceDiscovery = serviceDiscovery;
        this.friendlyNameProvider = friendlyNameProvider;
        this.navigator = navigator;

        _ = InitAsync();
    }

    #endregion

    #region Private methods

    private async Task InitAsync()
    {
        DeviceId = await secureStorage.GetStringAsync(DeviceIdKey);
        if (DeviceId is null)
        {
            DeviceId = friendlyNameProvider.FriendlyName ?? Guid.NewGuid().ToString();
            await secureStorage.SetStringAsync(DeviceIdKey, DeviceId);
        }

        IsPaired = await httpApiService.IsPairedAsync();

        serviceDiscovery.ServiceAdded += (_, e) =>
        {
            var address = e.Announcement.Address.IsIPv4MappedToIPv6
                ? e.Announcement.Address.MapToIPv4()
                : e.Announcement.Address;
            var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"[{address}]"
                : address.ToString();
            ServerUrl = $"https://{host}:{e.Announcement.Port}";
        };
        serviceDiscovery.ServiceRemoved += (_, _) => ServerUrl = null;
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RequestPairing()
    {
        Debug.Assert(ServerUrl is not null);
        Debug.Assert(DeviceId is not null);

        await httpApiService.RequestPairingAsync(ServerUrl, DeviceId);
        IsRequestSent =  true;
    }

    [RelayCommand]
    private async Task ConfirmPairing()
    {
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

        navigator.OpenPreviousPage();
    }

    [RelayCommand]
    private async Task Unpair()
    {
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
        serviceDiscovery.StartBrowse(SynchronizationServerService.ServiceType);
    }

    [RelayCommand]
    private void Unloaded()
    {
        serviceDiscovery.StopBrowse();
    }

    [RelayCommand]
    private void OpenPreviousPage() => navigator.OpenPreviousPage();

    #endregion Commands
}
