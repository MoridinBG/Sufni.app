using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Models;
using Sufni.App.Services;
using Serilog;

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
    private static readonly ILogger logger = Log.ForContext<PairingClientCoordinator>();

    private const string DeviceIdKey = "DeviceId";
    private const string DisplayNameKey = "DisplayName";

    private readonly ISecureStorage secureStorage;
    private readonly IHttpApiService httpApiService;
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IFriendlyNameProvider friendlyNameProvider;
    private readonly IShellCoordinator shell;
    private readonly List<string> discoveredServerUrls = [];

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
        var hadStoredDeviceId = deviceId is not null;
        if (deviceId is null)
        {
            deviceId = Guid.NewGuid().ToString();
            await secureStorage.SetStringAsync(DeviceIdKey, deviceId);
        }

        logger.Verbose(
            "Pairing client initialized device identity with stored value present {HadStoredDeviceId}",
            hadStoredDeviceId);
        DeviceIdChanged?.Invoke(this, EventArgs.Empty);

        // DisplayName falls back to the platform friendly name (which
        // may itself be null) when nothing has been committed yet. The
        // fallback is not persisted — it stays a default until the user
        // successfully pairs with one, at which point ConfirmPairingAsync
        // commits it back.
        var storedDisplayName = PairedDevice.NormalizeDisplayName(
            await secureStorage.GetStringAsync(DisplayNameKey));
        var fallbackDisplayName = storedDisplayName is null
            ? PairedDevice.NormalizeDisplayName(friendlyNameProvider.FriendlyName)
            : null;

        displayName = storedDisplayName ?? fallbackDisplayName;
        logger.Verbose(
            "Pairing client resolved display name with committed value present {HasCommittedDisplayName} and used fallback value present {HasFallbackDisplayName}",
            storedDisplayName is not null,
            fallbackDisplayName is not null);
        DisplayNameChanged?.Invoke(this, EventArgs.Empty);

        isPaired = await httpApiService.IsPairedAsync();
        logger.Verbose("Pairing client startup probe reported paired state {IsPaired}", isPaired);
        IsPairedChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string BuildServerUrl(ServiceAnnouncement announcement)
    {
        var address = announcement.Address.IsIPv4MappedToIPv6
            ? announcement.Address.MapToIPv4()
            : announcement.Address;
        var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
        return $"https://{host}:{announcement.Port}";
    }

    private void SetServerUrl(string? value)
    {
        if (serverUrl == value)
        {
            return;
        }

        serverUrl = value;
        ServerUrlChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
    {
        var discoveredServerUrl = BuildServerUrl(e.Announcement);
        discoveredServerUrls.Remove(discoveredServerUrl);
        discoveredServerUrls.Add(discoveredServerUrl);

        logger.Verbose("Pairing server discovered at {ServerUrl}", discoveredServerUrl);
        SetServerUrl(discoveredServerUrl);
    }

    private void OnServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
    {
        var removedServerUrl = BuildServerUrl(e.Announcement);
        logger.Verbose("Pairing server removed from discovery for {ServerUrl}", removedServerUrl);

        discoveredServerUrls.Remove(removedServerUrl);
        if (serverUrl != removedServerUrl)
        {
            return;
        }

        SetServerUrl(discoveredServerUrls.Count > 0 ? discoveredServerUrls[^1] : null);
    }

    public void StartBrowsing()
    {
        logger.Verbose("Starting pairing server browse");
        serviceDiscovery.StartBrowse(SynchronizationProtocol.ServiceType);
    }

    public void StopBrowsing()
    {
        logger.Verbose("Stopping pairing server browse");
        serviceDiscovery.StopBrowse();
    }

    public async Task<RequestPairingResult> RequestPairingAsync(string? displayName)
    {
        await Initialization;

        logger.Information("Starting pairing request");

        if (serverUrl is null)
        {
            logger.Warning("Pairing request could not start because no server is currently discovered");
            return new RequestPairingResult.Failed("No server discovered.");
        }

        var normalized = PairedDevice.NormalizeDisplayName(displayName);
        logger.Verbose(
            "Pairing request normalized display name present {HasDisplayName}",
            normalized is not null);
        try
        {
            await httpApiService.RequestPairingAsync(serverUrl, deviceId!, normalized);
            logger.Information("Pairing request sent");
            return new RequestPairingResult.Sent();
        }
        catch (Exception e)
        {
            logger.Error(e, "Pairing request failed");
            return new RequestPairingResult.Failed(e.Message);
        }
    }

    public async Task<ConfirmPairingResult> ConfirmPairingAsync(string? displayName, string pin)
    {
        await Initialization;

        logger.Information("Starting pairing confirmation");

        var normalized = PairedDevice.NormalizeDisplayName(displayName);
        logger.Verbose(
            "Pairing confirmation normalized display name present {HasDisplayName}",
            normalized is not null);
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

            logger.Information("Pairing confirmation completed");
            return new ConfirmPairingResult.Paired();
        }
        catch (Exception e)
        {
            logger.Error(e, "Pairing confirmation failed");
            return new ConfirmPairingResult.Failed(e.Message);
        }
    }

    public async Task<UnpairResult> UnpairAsync()
    {
        await Initialization;

        logger.Information("Starting client unpair");

        UnpairResult result;
        try
        {
            await httpApiService.UnpairAsync(deviceId!);
            result = new UnpairResult.Unpaired();
        }
        catch (HttpRequestException e)
        {
            logger.Warning(e, "Client unpair completed locally only");
            result = new UnpairResult.LocalOnly(e.Message);
        }
        catch (Exception e)
        {
            logger.Error(e, "Client unpair failed");
            result = new UnpairResult.Failed(e.Message);
        }

        // HttpApiService clears local credentials before the network
        // call, so we are locally unpaired regardless of outcome.
        isPaired = false;
        IsPairedChanged?.Invoke(this, EventArgs.Empty);

        if (result is UnpairResult.Unpaired)
        {
            logger.Information("Client unpair completed");
        }

        return result;
    }
}
