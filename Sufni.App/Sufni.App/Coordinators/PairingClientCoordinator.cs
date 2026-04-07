using System;
using System.Net.Http;
using System.Threading.Tasks;
using FriendlyNameProvider;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using ServiceDiscovery;
using Sufni.App.Services;

namespace Sufni.App.Coordinators;

/// <summary>
/// Mobile-only singleton owning DeviceId / ServerUrl / IsPaired
/// state and the pair/confirm/unpair HTTP plumbing. Replaces the
/// equivalent code that previously lived inside
/// <c>PairingClientViewModel</c>. The view model becomes a thin
/// observer of this coordinator.
/// </summary>
public sealed class PairingClientCoordinator : IPairingClientCoordinator
{
    private const string DeviceIdKey = "DeviceId";

    private readonly ISecureStorage secureStorage;
    private readonly IHttpApiService httpApiService;
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IFriendlyNameProvider friendlyNameProvider;
    private readonly IShellCoordinator shell;

    private string? deviceId;
    private string? serverUrl;
    private bool isPaired;

    public string? DeviceId => deviceId;
    public string? ServerUrl => serverUrl;
    public bool IsPaired => isPaired;

    public event EventHandler? DeviceIdChanged;
    public event EventHandler? ServerUrlChanged;
    public event EventHandler? IsPairedChanged;

    public PairingClientCoordinator(
        ISecureStorage secureStorage,
        IHttpApiService httpApiService,
        [FromKeyedServices("sync")] IServiceDiscovery serviceDiscovery,
        IFriendlyNameProvider friendlyNameProvider,
        IShellCoordinator shell)
    {
        this.secureStorage = secureStorage;
        this.httpApiService = httpApiService;
        this.serviceDiscovery = serviceDiscovery;
        this.friendlyNameProvider = friendlyNameProvider;
        this.shell = shell;

        serviceDiscovery.ServiceAdded += OnServiceAdded;
        serviceDiscovery.ServiceRemoved += OnServiceRemoved;

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        deviceId = await secureStorage.GetStringAsync(DeviceIdKey);
        if (deviceId is null)
        {
            deviceId = friendlyNameProvider.FriendlyName ?? Guid.NewGuid().ToString();
            await secureStorage.SetStringAsync(DeviceIdKey, deviceId);
        }
        DeviceIdChanged?.Invoke(this, EventArgs.Empty);

        isPaired = await httpApiService.IsPairedAsync();
        IsPairedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
    {
        var address = e.Announcement.Address.IsIPv4MappedToIPv6
            ? e.Announcement.Address.MapToIPv4()
            : e.Announcement.Address;
        var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
        serverUrl = $"https://{host}:{e.Announcement.Port}";
        ServerUrlChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
    {
        serverUrl = null;
        ServerUrlChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartBrowsing()
    {
        serviceDiscovery.StartBrowse(SynchronizationServerService.ServiceType);
    }

    public void StopBrowsing()
    {
        serviceDiscovery.StopBrowse();
    }

    public async Task<RequestPairingResult> RequestPairingAsync(string deviceId)
    {
        if (serverUrl is null)
        {
            return new RequestPairingResult.Failed("No server discovered.");
        }

        try
        {
            await httpApiService.RequestPairingAsync(serverUrl, deviceId);
            return new RequestPairingResult.Sent();
        }
        catch (Exception e)
        {
            return new RequestPairingResult.Failed(e.Message);
        }
    }

    public async Task<ConfirmPairingResult> ConfirmPairingAsync(string deviceId, string pin)
    {
        try
        {
            await httpApiService.ConfirmPairingAsync(deviceId, pin);

            // If the user edited the DeviceId before confirming, persist
            // the paired-under value so a later Unpair targets the right
            // row instead of the old canonical id (the server returns
            // 200 for unknown ids, leaving the desktop's row stale).
            if (this.deviceId != deviceId)
            {
                this.deviceId = deviceId;
                await secureStorage.SetStringAsync(DeviceIdKey, deviceId);
                DeviceIdChanged?.Invoke(this, EventArgs.Empty);
            }

            isPaired = true;
            IsPairedChanged?.Invoke(this, EventArgs.Empty);
            shell.GoBack();
            return new ConfirmPairingResult.Paired();
        }
        catch (Exception e)
        {
            return new ConfirmPairingResult.Failed(e.Message);
        }
    }

    public async Task<UnpairResult> UnpairAsync(string deviceId)
    {
        try
        {
            await httpApiService.UnpairAsync(deviceId);
            // HttpApiService clears local credentials before the network
            // call, so a successful return means both halves succeeded.
            isPaired = false;
            IsPairedChanged?.Invoke(this, EventArgs.Empty);
            return new UnpairResult.Unpaired();
        }
        catch (HttpRequestException e)
        {
            // Local credentials are already gone (HttpApiService.UnpairAsync
            // wipes them before issuing the network call), so the device
            // is locally unpaired regardless of the network failure.
            isPaired = false;
            IsPairedChanged?.Invoke(this, EventArgs.Empty);
            return new UnpairResult.LocalOnly(e.Message);
        }
        catch (Exception e)
        {
            // For consistency with the existing behaviour: HttpApiService
            // clears credentials before the network call, so we treat
            // the local state as unpaired here too.
            isPaired = false;
            IsPairedChanged?.Invoke(this, EventArgs.Empty);
            return new UnpairResult.Failed(e.Message);
        }
    }
}
