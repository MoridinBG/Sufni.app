using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Foundation;
using CoreFoundation;
using Network;
using Serilog;
using Sufni.App.Infrastructure;

namespace Sufni.App.AppleShared;

public abstract class AppleBonjourServiceDiscoveryBase : IServiceDiscovery, IAsyncDisposable
{
    private readonly ILogger logger;
    private readonly string platformName;
    private readonly DispatchQueue dispatchQueue = DispatchQueue.MainQueue;
    private readonly ServiceDiscoveryResolution resolution;
    private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposed;
    private readonly BonjourBrowseLifecycle<Action> browseLifecycle = new();
    private readonly NWParameters parameters = new()
    {
        LocalOnly = true,
        ReuseLocalAddress = true,
        FastOpenEnabled = true,
    };

    private NWBrowser? browser;

    protected AppleBonjourServiceDiscoveryBase(string platformName, ServiceDiscoveryResolution resolution)
    {
        this.platformName = platformName;
        this.resolution = resolution;
        logger = Log.ForContext(GetType());
    }

    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    public void StartBrowse(string type)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!browseLifecycle.TryStart())
        {
            logger.Verbose("Ignoring Bonjour browse start on {Platform} for {ServiceType} because browsing is already active", platformName, type);
            return;
        }

        logger.Verbose("Starting Bonjour browse on {Platform} for {ServiceType}", platformName, type);
        var newBrowser = CreateBrowser(type);

        try
        {
            browser = newBrowser;
            newBrowser.Start();
        }
        catch
        {
            browser = null;
            newBrowser.Cancel();
            newBrowser.Dispose();
            browseLifecycle.TryStop(static cancel => cancel(), PublishRemoved);
            throw;
        }
    }

    public void StopBrowse()
    {
        if (!browseLifecycle.TryStop(static cancel => cancel(), PublishRemoved))
        {
            logger.Verbose("Ignoring Bonjour browse stop on {Platform} because browsing is not active", platformName);
            return;
        }

        logger.Verbose("Stopping Bonjour browse on {Platform}", platformName);
        Debug.Assert(browser is not null);
        var stoppedBrowser = browser;
        browser = null;
        dispatchQueue.DispatchAsync(() =>
        {
            stoppedBrowser?.Cancel();
            stoppedBrowser?.Dispose();
        });
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            StopBrowse();
            dispatchQueue.DispatchAsync(() =>
            {
                parameters.Dispose();
                disposal.TrySetResult();
            });
        }
        return new ValueTask(disposal.Task);
    }

    private void PublishAdded(ServiceAnnouncement announcement) =>
        ServiceAdded?.Invoke(this, new ServiceAnnouncementEventArgs(announcement));

    private void PublishRemoved(ServiceAnnouncement announcement) =>
        ServiceRemoved?.Invoke(this, new ServiceAnnouncementEventArgs(announcement));

    private void OnServiceAdded(NWBrowseResult? result)
    {
        if (result is null)
        {
            logger.Verbose("Ignoring empty Bonjour browse result on {Platform}", platformName);
            return;
        }

        var key = GetBrowseResultKey(result);
        var txtRecords = ReadTxtRecords(result.TxtRecord);
        logger.Verbose(
            "Bonjour browse result added on {Platform} for endpoint {Endpoint}",
            platformName,
            key);
        if (resolution == ServiceDiscoveryResolution.AnnouncedAddresses)
        {
            ResolveAddresses(result.EndPoint, key, txtRecords);
            return;
        }

        var connection = new NWConnection(result.EndPoint, NWParameters.CreateTcp());
        if (!browseLifecycle.TryTrackPending(key, () => connection.Cancel(), static cancel => cancel(), out var resolutionId))
        {
            logger.Verbose(
                "Ignoring Bonjour browse result on {Platform} because browsing is no longer active",
                platformName);
            return;
        }

        connection.SetStateChangeHandler((state, error) =>
        {
            logger.Verbose(
                "Bonjour resolution state on {Platform} for {Endpoint} is {State} error {Error}",
                platformName,
                key,
                state,
                error?.ErrorCode);

            if (state != NWConnectionState.Ready)
            {
                return;
            }

            var endpoint = connection.CurrentPath?.EffectiveRemoteEndpoint;
            var address = endpoint is not null ? IPAddress.Parse(endpoint.Address) : null;
            var port = endpoint?.PortNumber;
            connection.Cancel();

            if (address is null || port is null)
            {
                logger.Verbose("Bonjour browse result on {Platform} did not resolve to a connectable endpoint", platformName);
                return;
            }

            var announcement = new ServiceAnnouncement(address, port.Value, key, txtRecords);
            if (!browseLifecycle.TryResolve(key, resolutionId, announcement))
            {
                logger.Verbose(
                    "Ignoring Bonjour resolution on {Platform} for {Address}:{Port} because the browse result was removed or restarted before resolution completed",
                    platformName,
                    address,
                    port);
                return;
            }

            logger.Verbose(
                "Resolved Bonjour service on {Platform} to {Address}:{Port}",
                platformName,
                address,
                port);
            ServiceAdded?.Invoke(this, new ServiceAnnouncementEventArgs(announcement));
        });
        connection.SetQueue(dispatchQueue);
        connection.Start();
    }

    private void ResolveAddresses(NWEndpoint endpoint, string key, IReadOnlyDictionary<string, string> txtRecords)
    {
        // Bonjour resolution uses the main run loop; it never opens the DAQ's TCP port.
        var resolver = new NSNetService(endpoint.BonjourServiceDomain, endpoint.BonjourServiceType, endpoint.BonjourServiceName);
        Action cancel = () => dispatchQueue.DispatchAsync(() =>
        {
            resolver.Stop();
            resolver.Dispose();
        });
        if (!browseLifecycle.TryTrackPending(key, cancel, static action => action(), out var resolutionId)) return;

        resolver.AddressResolved += (_, _) =>
        {
            var addresses = (resolver.Addresses ?? []).Select(data => BonjourSocketAddress.Read(data.ToArray()))
                .OfType<IPAddress>().ToArray();
            if (addresses.Length == 0) return;
            var announcement = new ServiceAnnouncement(new ServiceEndpoint(addresses, checked((int)resolver.Port)), key, txtRecords);
            browseLifecycle.PublishResolved(key, resolutionId, announcement, PublishAdded);
        };
        resolver.ResolveFailure += (_, _) => browseLifecycle.CompletePending(key, resolutionId, static action => action());
        resolver.Stopped += (_, _) => browseLifecycle.CompletePending(key, resolutionId, static action => action());
        resolver.Resolve(5);
    }

    private void OnServiceRemoved(NWBrowseResult? result)
    {
        if (result is null)
        {
            logger.Verbose("Ignoring empty Bonjour removal on {Platform}", platformName);
            return;
        }

        var key = GetBrowseResultKey(result);
        browseLifecycle.CancelPending(key, static cancel => cancel());

        if (!browseLifecycle.TryRemoveResolved(key, out var announcement))
        {
            logger.Verbose("Ignoring Bonjour removal on {Platform} because the service was never resolved", platformName);
            return;
        }

        logger.Verbose(
            "Removed Bonjour service on {Platform} for {Address}:{Port}",
            platformName,
            announcement!.Address,
            announcement.Port);
        PublishRemoved(announcement);
    }

    private NWBrowser CreateBrowser(string type)
    {
        using var browserDescriptor = NWBrowserDescriptor.CreateBonjourService(type, "local.");
        browserDescriptor.IncludeTxtRecord = true;
        var newBrowser = new NWBrowser(browserDescriptor, parameters);
        newBrowser.SetDispatchQueue(dispatchQueue);

        newBrowser.SetStateChangesHandler((state, error) =>
        {
            logger.Verbose(
                "Bonjour browser state on {Platform} for {ServiceType} is {State} error {Error}",
                platformName,
                type,
                state,
                error?.ErrorCode);
        });

        newBrowser.CompleteChangesDelegate = changes =>
        {
            if (changes is null)
            {
                logger.Verbose("Bonjour browse changes batch on {Platform} for {ServiceType} was null", platformName, type);
                return;
            }

            logger.Verbose(
                "Bonjour browse changes batch on {Platform} for {ServiceType} count {Count}",
                platformName,
                type,
                changes.Count);

            foreach (var change in changes)
            {
                logger.Verbose(
                    "Bonjour browse change on {Platform} for {ServiceType} kind {Kind} endpoint {Endpoint}",
                    platformName,
                    type,
                    change.change,
                    change.result?.EndPoint);

                switch (change.change)
                {
                    case NWBrowseResultChange.ResultAdded:
                        OnServiceAdded(change.result);
                        break;
                    case NWBrowseResultChange.ResultRemoved:
                        OnServiceRemoved(change.result);
                        break;
                }
            }
        };

        return newBrowser;
    }

    private static string GetBrowseResultKey(NWBrowseResult result) => result.EndPoint.ToString() ?? string.Empty;

    private static IReadOnlyDictionary<string, string> ReadTxtRecords(NWTxtRecord? txtRecord)
    {
        var copiedRecords = new List<KeyValuePair<string, byte[]>>();
        txtRecord?.Apply((key, _, value) =>
        {
            if (!string.IsNullOrEmpty(key))
            {
                copiedRecords.Add(new KeyValuePair<string, byte[]>(key, value.ToArray()));
            }

            return true;
        });

        return ServiceAnnouncementMetadataReader.ReadTxtRecords(copiedRecords);
    }
}
