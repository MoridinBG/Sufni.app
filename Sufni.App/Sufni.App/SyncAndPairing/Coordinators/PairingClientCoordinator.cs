using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Coordinators;

/// <summary>Owns mobile pairing and peer discovery.</summary>
public sealed class PairingClientCoordinator : IPairingClientCoordinator
{
    private static readonly ILogger logger = Log.ForContext<PairingClientCoordinator>();

    private const string DisplayNameKey = "DisplayName";

    private readonly ISecureStorage secureStorage;
    private readonly ILocalDeviceIdentity localDeviceIdentity;
    private readonly IHttpApiService httpApiService;
    private readonly IServiceDiscovery serviceDiscovery;
    private readonly IFriendlyNameProvider friendlyNameProvider;
    private readonly TimeProvider timeProvider;
    private readonly List<string> discoveredServerUrls = [];
    private readonly System.Threading.Lock browseGate = new();

    private string? deviceId;
    private string? displayName;
    private string? serverUrl;
    private int browseReferenceCount;
    private bool isBrowseStarted;
    private TaskCompletionSource<string?>? pendingServerResolution;

    private Task Initialization { get; }

    public string? DeviceId => deviceId;
    public string? DisplayName => displayName;
    public string? ServerUrl => serverUrl;
    public bool IsPaired { get; private set; }
    public IObservable<bool> PairedState => httpApiService.PairedState;

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
        TimeProvider timeProvider,
        ILocalDeviceIdentity localDeviceIdentity)
    {
        this.secureStorage = secureStorage;
        this.httpApiService = httpApiService;
        this.serviceDiscovery = serviceDiscovery;
        this.friendlyNameProvider = friendlyNameProvider;
        this.timeProvider = timeProvider;
        this.localDeviceIdentity = localDeviceIdentity;

        _ = httpApiService.PairedState.Subscribe(OnPairedStateChanged);
        serviceDiscovery.ServiceAdded += OnServiceAdded;
        serviceDiscovery.ServiceRemoved += OnServiceRemoved;

        Initialization = Init();
    }

    private async Task Init()
    {
        deviceId = await localDeviceIdentity.GetIdAsync();
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

        _ = await httpApiService.IsPairedAsync();
        logger.Verbose("Pairing client startup probe reported paired state {IsPaired}", IsPaired);
    }

    private static string? BuildServerUrl(ServiceAnnouncement announcement)
    {
        var address = announcement.Address.IsIPv4MappedToIPv6
            ? announcement.Address.MapToIPv4()
            : announcement.Address;
        if (address.IsIPv6LinkLocal)
        {
            return null;
        }

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

        if (value is not null)
        {
            pendingServerResolution?.TrySetResult(value);
        }
    }

    private void OnServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
    {
        var discoveredServerUrl = BuildServerUrl(e.Announcement);
        if (discoveredServerUrl is null)
        {
            return;
        }

        discoveredServerUrls.Remove(discoveredServerUrl);
        discoveredServerUrls.Add(discoveredServerUrl);

        logger.Verbose("Pairing server discovered at {ServerUrl}", discoveredServerUrl);
        SetServerUrl(discoveredServerUrl);
    }

    private void OnServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
    {
        var removedServerUrl = BuildServerUrl(e.Announcement);
        if (removedServerUrl is null)
        {
            return;
        }

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
        lock (browseGate)
        {
            browseReferenceCount++;
            if (isBrowseStarted)
            {
                return;
            }

            isBrowseStarted = true;
        }

        try
        {
            logger.Verbose("Starting pairing server browse");
            serviceDiscovery.StartBrowse(SynchronizationProtocol.ServiceType);
        }
        catch
        {
            lock (browseGate)
            {
                browseReferenceCount--;
                isBrowseStarted = false;
            }

            throw;
        }
    }

    public void StopBrowsing()
    {
        lock (browseGate)
        {
            if (browseReferenceCount == 0)
            {
                return;
            }

            browseReferenceCount--;
            if (browseReferenceCount > 0)
            {
                return;
            }

            isBrowseStarted = false;
        }

        logger.Verbose("Stopping pairing server browse");
        serviceDiscovery.StopBrowse();
    }

    public async Task<ISynchronizationPeer?> ResolvePeerAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);

        if (!IsPaired)
        {
            return null;
        }

        if (serverUrl is not null)
        {
            return await httpApiService.GetPeerAsync(new PeerEndpoint(serverUrl));
        }

        var resolution = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingServerResolution = resolution;

        var browseStarted = false;
        try
        {
            StartBrowsing();
            browseStarted = true;

            string? resolvedServerUrl;
            try { resolvedServerUrl = await resolution.Task.WaitAsync(timeout, timeProvider, cancellationToken); }
            catch (TimeoutException) { return null; }

            return resolvedServerUrl is null ? null
                : await httpApiService.GetPeerAsync(new PeerEndpoint(resolvedServerUrl));
        }
        finally
        {
            if (ReferenceEquals(pendingServerResolution, resolution))
            {
                pendingServerResolution = null;
            }

            if (browseStarted)
            {
                StopBrowsing();
            }
        }
    }

    public async Task<RequestPairingResult> RequestPairingAsync(string? displayName)
    {
        await Initialization;

        logger.Information("Starting pairing request");

        if (serverUrl is null)
        {
            return new RequestPairingResult.ServerUnavailable();
        }

        var normalized = PairedDevice.NormalizeDisplayName(displayName);
        logger.Verbose(
            "Pairing request normalized display name present {HasDisplayName}",
            normalized is not null);
        try
        {
            await httpApiService.RequestPairingAsync(new PeerEndpoint(serverUrl), deviceId!, normalized);
            logger.Information("Pairing request sent");
            return new RequestPairingResult.Sent();
        }
        catch (Exception e)
        {
            logger.Error(e, "Pairing request failed for {DeviceId} at {ServerUrl}", deviceId, serverUrl);
            return new RequestPairingResult.Failed(PairingFailureReason.Request, e.Message);
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

            PairingConfirmed?.Invoke(this, EventArgs.Empty);

            logger.Information("Pairing confirmation completed");
            return new ConfirmPairingResult.Paired();
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new ConfirmPairingResult.PinRejected();
        }
        catch (Exception e)
        {
            logger.Error(e, "Pairing confirmation failed for {DeviceId} at {ServerUrl}", deviceId, serverUrl);
            return new ConfirmPairingResult.Failed(PairingFailureReason.Confirm, e.Message);
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
            logger.Warning(e, "Client unpair completed locally only for {DeviceId} at {ServerUrl}", deviceId, serverUrl);
            result = new UnpairResult.LocalOnly();
        }
        catch (Exception e)
        {
            logger.Error(e, "Client unpair failed for {DeviceId} at {ServerUrl}", deviceId, serverUrl);
            result = new UnpairResult.Failed(PairingFailureReason.Unpair, e.Message);
        }

        if (result is UnpairResult.Unpaired)
        {
            logger.Information("Client unpair completed");
        }

        return result;
    }

    private void OnPairedStateChanged(bool value)
    {
        if (IsPaired == value)
        {
            return;
        }

        IsPaired = value;
        IsPairedChanged?.Invoke(this, EventArgs.Empty);
    }
}
