using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using CoreFoundation;
using Network;
using Serilog;
using Sufni.App.Services;

namespace Sufni.App.AppleShared;

public abstract class AppleBonjourServiceDiscoveryBase : IServiceDiscovery
{
    private readonly ILogger logger;
    private readonly string platformName;
    private readonly DispatchQueue dispatchQueue = new("com.sghctoma.sufni-bridge.serviceDiscovery");
    private readonly BonjourBrowseLifecycle<NWConnection> browseLifecycle = new();
    private readonly NWParameters parameters = new()
    {
        LocalOnly = true,
        ReuseLocalAddress = true,
        FastOpenEnabled = true,
    };

    private NWBrowser? browser;

    protected AppleBonjourServiceDiscoveryBase(string platformName)
    {
        this.platformName = platformName;
        logger = Log.ForContext(GetType());
    }

    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    public void StartBrowse(string type)
    {
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
            browseLifecycle.TryStop(static connection => connection.Cancel());
            throw;
        }
    }

    public void StopBrowse()
    {
        if (!browseLifecycle.TryStop(static connection => connection.Cancel()))
        {
            logger.Verbose("Ignoring Bonjour browse stop on {Platform} because browsing is not active", platformName);
            return;
        }

        logger.Verbose("Stopping Bonjour browse on {Platform}", platformName);
        Debug.Assert(browser is not null);
        browser?.Cancel();
        browser = null;
    }

    private void OnServiceAdded(NWBrowseResult? result)
    {
        if (result is null)
        {
            logger.Verbose("Ignoring empty Bonjour browse result on {Platform}", platformName);
            return;
        }

        var key = GetBrowseResultKey(result);
        var connection = new NWConnection(result.EndPoint, NWParameters.CreateTcp());
        if (!browseLifecycle.TryTrackPending(key, connection, static pendingConnection => pendingConnection.Cancel(), out var resolutionId))
        {
            logger.Verbose(
                "Ignoring Bonjour browse result on {Platform} because browsing is no longer active",
                platformName);
            return;
        }

        connection.SetStateChangeHandler((state, _) =>
        {
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

            var announcement = new ServiceAnnouncement(address, port.Value);
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

    private void OnServiceRemoved(NWBrowseResult? result)
    {
        if (result is null)
        {
            logger.Verbose("Ignoring empty Bonjour removal on {Platform}", platformName);
            return;
        }

        var key = GetBrowseResultKey(result);
        if (browseLifecycle.CancelPending(key, static pendingConnection => pendingConnection.Cancel()))
        {
            logger.Verbose("Canceled pending Bonjour resolution on {Platform} because the service was removed before resolution completed", platformName);
            return;
        }

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
        ServiceRemoved?.Invoke(this, new ServiceAnnouncementEventArgs(announcement));
    }

    private NWBrowser CreateBrowser(string type)
    {
        var browserDescriptor = NWBrowserDescriptor.CreateBonjourService(type, "local.");
        var newBrowser = new NWBrowser(browserDescriptor, parameters);
        newBrowser.SetDispatchQueue(dispatchQueue);

        newBrowser.CompleteChangesDelegate = changes =>
        {
            if (changes is null)
            {
                return;
            }

            foreach (var change in changes)
            {
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
}