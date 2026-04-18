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
    private readonly Dictionary<string, ServiceAnnouncement> announcementsByResult = [];
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
        logger.Verbose("Starting Bonjour browse on {Platform} for {ServiceType}", platformName, type);
        browser ??= CreateBrowser(type);
        browser.Start();
    }

    public void StopBrowse()
    {
        Debug.Assert(browser is not null);
        logger.Verbose("Stopping Bonjour browse on {Platform}", platformName);
        browser.Cancel();
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
            announcementsByResult[key] = announcement;

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
        if (!announcementsByResult.Remove(key, out var announcement))
        {
            logger.Verbose("Ignoring Bonjour removal on {Platform} because the service was never resolved", platformName);
            return;
        }

        logger.Verbose(
            "Removed Bonjour service on {Platform} for {Address}:{Port}",
            platformName,
            announcement.Address,
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