using System;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using Serilog;
using Tmds.MDns;

namespace Sufni.App.Infrastructure;

public class SocketServiceDiscovery : IServiceDiscovery
{
    private static readonly ILogger logger = Log.ForContext<SocketServiceDiscovery>();

    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    private readonly ServiceBrowser browser = new();
    private readonly SocketServiceDiscoveryEndpointCache endpointCache = new();

    public SocketServiceDiscovery()
    {
        browser.ServiceAdded += (sender, args) =>
        {
            var announcedAddresses = args.Announcement.Addresses.ToArray();
            var connectableAddress = TryConnect(announcedAddresses, args.Announcement.Port);
            if (connectableAddress is null)
            {
                logger.Verbose(
                    "Ignoring discovered socket service on port {Port} because no connectable endpoint was resolved",
                    args.Announcement.Port);
                return;
            }

            var instanceName = ServiceAnnouncementMetadataReader.ReadInstanceName(args.Announcement);
            var txtRecords = ServiceAnnouncementMetadataReader.ReadTxtRecords(args.Announcement);
            endpointCache.Add(instanceName, announcedAddresses, args.Announcement.Port, connectableAddress);
            var announcement = new ServiceAnnouncement(connectableAddress, args.Announcement.Port, instanceName, txtRecords);
            logger.Verbose(
                "Discovered socket service endpoint {Address}:{Port} instance {InstanceName}",
                connectableAddress,
                args.Announcement.Port,
                instanceName);
            ServiceAdded?.Invoke(sender, new ServiceAnnouncementEventArgs(announcement));
        };

        browser.ServiceRemoved += (sender, args) =>
        {
            var instanceName = ServiceAnnouncementMetadataReader.ReadInstanceName(args.Announcement);
            if (!endpointCache.TryRemove(instanceName, args.Announcement.Addresses, args.Announcement.Port, out var removedAddress))
            {
                logger.Verbose(
                    "Ignoring removed socket service on port {Port} because no matching added endpoint was emitted",
                    args.Announcement.Port);
                return;
            }

            var txtRecords = ServiceAnnouncementMetadataReader.ReadTxtRecords(args.Announcement);
            var announcement = new ServiceAnnouncement(removedAddress, args.Announcement.Port, instanceName, txtRecords);
            logger.Verbose(
                "Removed socket service endpoint {Address}:{Port} instance {InstanceName}",
                removedAddress,
                args.Announcement.Port,
                instanceName);
            ServiceRemoved?.Invoke(sender, new ServiceAnnouncementEventArgs(announcement));
        };
    }

    public void StartBrowse(string type)
    {
        logger.Verbose("Starting socket service browse for {ServiceType}", type);
        browser.StartBrowse(type);
    }

    public void StopBrowse()
    {
        logger.Verbose("Stopping socket service browse");
        browser.StopBrowse();
    }

    private static IPAddress? TryConnect(IPAddress[] addresses, int port, int timeout = 2000)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(addresses, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(timeout);
            if (!success) return null;

            client.EndConnect(result);
            return ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "TCP probe failed for port {Port}", port);
            return null;
        }
    }
}
