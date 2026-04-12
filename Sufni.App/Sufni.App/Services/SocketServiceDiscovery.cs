using System;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using Serilog;
using Tmds.MDns;

namespace Sufni.App.Services;

public class SocketServiceDiscovery : IServiceDiscovery
{
    private static readonly ILogger logger = Log.ForContext<SocketServiceDiscovery>();

    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    private readonly ServiceBrowser browser = new();

    public SocketServiceDiscovery()
    {
        browser.ServiceAdded += (sender, args) =>
        {
            var connectableAddress = TryConnect(args.Announcement.Addresses.ToArray(), args.Announcement.Port);
            if (connectableAddress is null)
            {
                logger.Verbose(
                    "Ignoring discovered socket service on port {Port} because no connectable endpoint was resolved",
                    args.Announcement.Port);
                return;
            }

            var announcement = new ServiceAnnouncement(connectableAddress, args.Announcement.Port);
            logger.Verbose(
                "Discovered socket service endpoint {Address}:{Port}",
                connectableAddress,
                args.Announcement.Port);
            ServiceAdded?.Invoke(sender, new ServiceAnnouncementEventArgs(announcement));
        };

        browser.ServiceRemoved += (sender, args) =>
        {
            var announcement = new ServiceAnnouncement(args.Announcement.Addresses[0], args.Announcement.Port);
            logger.Verbose(
                "Removed socket service endpoint {Address}:{Port}",
                args.Announcement.Addresses[0],
                args.Announcement.Port);
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
        catch
        {
            return null;
        }
    }
}