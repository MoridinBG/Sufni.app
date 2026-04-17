using System;
using System.Diagnostics;
using System.Net;
using CoreFoundation;
using Network;
using Serilog;
using Sufni.App.Services;

namespace Sufni.App.macOS;

public class BonjourServiceDiscovery : IServiceDiscovery
{
    private static readonly ILogger logger = Log.ForContext<BonjourServiceDiscovery>();

    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    private readonly DispatchQueue dispatchQueue = new("com.sghctoma.sufni-bridge.serviceDiscovery");
    private NWBrowser? browser;
    private IPAddress? currentIpAddress;
    private ushort? currentPort;

    private readonly NWParameters parameters = new()
    {
        LocalOnly = true,
        ReuseLocalAddress = true,
        FastOpenEnabled = true,
    };

    private void OnServiceAdded(NWBrowseResult? result)
    {
        if (result is null)
        {
            logger.Verbose("Ignoring empty Bonjour browse result on macOS");
            return;
        }

        var connection = new NWConnection(result.EndPoint, NWParameters.CreateTcp());
        connection.SetStateChangeHandler((state, _) =>
        {
            switch (state)
            {
                case NWConnectionState.Ready:
                    {
                        var endpoint = connection.CurrentPath?.EffectiveRemoteEndpoint;
                        currentIpAddress = endpoint is not null ? IPAddress.Parse(endpoint.Address) : null;
                        currentPort = endpoint?.PortNumber;
                        connection.Cancel();

                        if (currentIpAddress is null || currentPort is null)
                        {
                            logger.Verbose("Bonjour browse result on macOS did not resolve to a connectable endpoint");
                            return;
                        }

                        logger.Verbose(
                            "Resolved Bonjour service on macOS to {Address}:{Port}",
                            currentIpAddress,
                            currentPort);
                        ServiceAdded?.Invoke(this, new ServiceAnnouncementEventArgs(
                            new ServiceAnnouncement(currentIpAddress, currentPort.Value)));
                        break;
                    }
            }
        });
        connection.SetQueue(dispatchQueue);
        connection.Start();
    }

    private void OnServiceRemoved()
    {
        if (currentIpAddress is null || currentPort is null) return;

        logger.Verbose(
            "Removed Bonjour service on macOS for {Address}:{Port}",
            currentIpAddress,
            currentPort);
        ServiceRemoved?.Invoke(this, new ServiceAnnouncementEventArgs(
            new ServiceAnnouncement(currentIpAddress, currentPort.Value)));
    }

    private NWBrowser CreateBrowser(string type)
    {
        var browserDescriptor = NWBrowserDescriptor.CreateBonjourService(type, "local.");
        var newBrowser = new NWBrowser(browserDescriptor, parameters);
        newBrowser.SetDispatchQueue(dispatchQueue);

        newBrowser.CompleteChangesDelegate = changes =>
        {
            if (changes is null) return;

            foreach (var change in changes)
            {
                switch (change.change)
                {
                    case NWBrowseResultChange.ResultAdded:
                        OnServiceAdded(change.result);
                        break;
                    case NWBrowseResultChange.ResultRemoved:
                        OnServiceRemoved();
                        break;
                }
            }
        };

        return newBrowser;
    }

    public void StartBrowse(string type)
    {
        logger.Verbose("Starting Bonjour browse on macOS for {ServiceType}", type);
        browser ??= CreateBrowser(type);
        browser.Start();
    }

    public void StopBrowse()
    {
        Debug.Assert(browser is not null);
        logger.Verbose("Stopping Bonjour browse on macOS");
        browser.Cancel();
    }
}