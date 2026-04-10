using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ServiceDiscovery;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Coordinators;

/// <summary>
/// Mobile-only singleton owning DeviceId / DisplayName / ServerUrl /
/// IsPaired state and the pair/confirm/unpair HTTP plumbing. Replaces
/// the equivalent code that previously lived inside
/// <c>PairingClientViewModel</c>. The view model becomes a thin
/// observer of this coordinator.
/// </summary>
public sealed class PairingClientCoordinator : IPairingClientCoordinator
{
    private const string DeviceIdKey = "DeviceId";
    private const string DisplayNameKey = "DisplayName";

    private readonly ISecureStorage secureStorage;
    private readonly IHttpApiService httpApiService;
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IFriendlyNameProvider friendlyNameProvider;
    private readonly IShellCoordinator shell;

    private string? deviceId;
    private string? displayName;
    private string? serverUrl;
    private bool isPaired;

    private Task Initialization { get; }

    public string? DeviceId => deviceId;
    public string? DisplayName => displayName;
    public string? ServerUrl => serverUrl;
    public bool IsPaired => isPaired;

    public event EventHandler? DeviceIdChanged;
    public event EventHandler? DisplayNameChanged;
    public event EventHandler? ServerUrlChanged;
    public event EventHandler? IsPairedChanged;
    public event EventHandler? PairingConfirmed;

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

        Initialization = Init();
    }

    private async Task Init()
    {
        deviceId = await secureStorage.GetStringAsync(DeviceIdKey);
        if (deviceId is null)
        {
            deviceId = Guid.NewGuid().ToString();
            await secureStorage.SetStringAsync(DeviceIdKey, deviceId);
        }
        DeviceIdChanged?.Invoke(this, EventArgs.Empty);

        // DisplayName falls back to the platform friendly name (which
        // may itself be null) when nothing has been committed yet. The
        // fallback is not persisted — it stays a default until the user
        // successfully pairs with one, at which point ConfirmPairingAsync
        // commits it back.
        displayName = PairedDevice.NormalizeDisplayName(
                          await secureStorage.GetStringAsync(DisplayNameKey))
                      ?? PairedDevice.NormalizeDisplayName(friendlyNameProvider.FriendlyName);
        DisplayNameChanged?.Invoke(this, EventArgs.Empty);

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
        serviceDiscovery.StartBrowse(SynchronizationProtocol.ServiceType);
    }

    public void StopBrowsing()
    {
        serviceDiscovery.StopBrowse();
    }

    public async Task<RequestPairingResult> RequestPairingAsync(string? displayName)
    {
        await Initialization;

        if (serverUrl is null)
        {
            return new RequestPairingResult.Failed("No server discovered.");
        }

        var normalized = PairedDevice.NormalizeDisplayName(displayName);
        try
        {
            await httpApiService.RequestPairingAsync(serverUrl, deviceId!, normalized);
            return new RequestPairingResult.Sent();
        }
        catch (Exception e)
        {
            return new RequestPairingResult.Failed(e.Message);
        }
    }

    public async Task<ConfirmPairingResult> ConfirmPairingAsync(string? displayName, string pin)
    {
        await Initialization;

        var normalized = PairedDevice.NormalizeDisplayName(displayName);
        try
        {
            await httpApiService.ConfirmPairingAsync(deviceId!, normalized, pin);

            await secureStorage.SetStringAsync(DisplayNameKey, normalized);

            if (this.displayName != normalized)
            {
                this.displayName = normalized;
                DisplayNameChanged?.Invoke(this, EventArgs.Empty);
            }

            isPaired = true;
            IsPairedChanged?.Invoke(this, EventArgs.Empty);
            shell.GoBack();
            PairingConfirmed?.Invoke(this, EventArgs.Empty);
            return new ConfirmPairingResult.Paired();
        }
        catch (Exception e)
        {
            return new ConfirmPairingResult.Failed(e.Message);
        }
    }

    public async Task<UnpairResult> UnpairAsync()
    {
        await Initialization;

        UnpairResult result;
        try
        {
            await httpApiService.UnpairAsync(deviceId!);
            result = new UnpairResult.Unpaired();
        }
        catch (HttpRequestException e)
        {
            result = new UnpairResult.LocalOnly(e.Message);
        }
        catch (Exception e)
        {
            result = new UnpairResult.Failed(e.Message);
        }

        // HttpApiService clears local credentials before the network
        // call, so we are locally unpaired regardless of outcome.
        isPaired = false;
        IsPairedChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
