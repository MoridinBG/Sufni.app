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
    private readonly System.Threading.Lock browseGate = new();
    private bool isBrowsing;
    private long browseGeneration;

    public SocketServiceDiscovery(ServiceDiscoveryResolution resolution)
    {
        browser.ServiceAdded += (sender, args) =>
        {
            long generation;
            lock (browseGate)
            {
                if (!isBrowsing) return;
                generation = browseGeneration;
            }
            var announcedAddresses = args.Announcement.Addresses.ToArray();
            if (announcedAddresses.Length == 0) return;
            var endpoint = new ServiceEndpoint(announcedAddresses, args.Announcement.Port);
            if (resolution == ServiceDiscoveryResolution.ReachableTcpEndpoint)
            {
                var connectableAddress = TryConnect(announcedAddresses, args.Announcement.Port);
                if (connectableAddress is null) return;
                endpoint = new ServiceEndpoint([connectableAddress], args.Announcement.Port);
            }

            var instanceName = string.IsNullOrWhiteSpace(args.Announcement.Instance) ? null : args.Announcement.Instance;
            var txtRecords = ServiceAnnouncementMetadataReader.ReadTxtRecords(args.Announcement.Txt ?? []);
            var announcement = new ServiceAnnouncement(endpoint, instanceName, txtRecords);
            logger.Verbose(
                "Discovered socket service endpoint {Address}:{Port} instance {InstanceName}",
                endpoint.Host,
                args.Announcement.Port,
                instanceName);
            lock (browseGate)
            {
                if (!isBrowsing || generation != browseGeneration) return;
                endpointCache.Add(announcedAddresses, announcement);
                ServiceAdded?.Invoke(sender, new ServiceAnnouncementEventArgs(announcement));
            }
        };

        browser.ServiceRemoved += (sender, args) =>
        {
            lock (browseGate)
            {
                var instanceName = string.IsNullOrWhiteSpace(args.Announcement.Instance) ? null : args.Announcement.Instance;
                if (!endpointCache.TryRemove(instanceName, args.Announcement.Addresses, args.Announcement.Port, out var announcement))
                {
                    logger.Verbose(
                        "Ignoring removed socket service on port {Port} because no matching added endpoint was emitted",
                        args.Announcement.Port);
                    return;
                }

                logger.Verbose(
                    "Removed socket service endpoint {Address}:{Port} instance {InstanceName}",
                    announcement.Address,
                    args.Announcement.Port,
                    instanceName);
                ServiceRemoved?.Invoke(sender, new ServiceAnnouncementEventArgs(announcement));
            }
        };
    }

    public void StartBrowse(string type)
    {
        logger.Verbose("Starting socket service browse for {ServiceType}", type);
        lock (browseGate)
        {
            if (isBrowsing) return;
            isBrowsing = true;
            browseGeneration++;
        }
        try { browser.StartBrowse(type); }
        catch
        {
            lock (browseGate) isBrowsing = false;
            throw;
        }
    }

    public void StopBrowse()
    {
        logger.Verbose("Stopping socket service browse");
        lock (browseGate)
        {
            isBrowsing = false;
            foreach (var announcement in endpointCache.Drain())
                ServiceRemoved?.Invoke(this, new ServiceAnnouncementEventArgs(announcement));
        }
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
