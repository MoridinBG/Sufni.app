using System.Net;
using System.Net.Sockets;
using Tmds.MDns;

namespace ServiceDiscovery;

public class ServiceDiscovery : IServiceDiscovery
{
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;
    
    private readonly ServiceBrowser browser = new(); 
    
    public ServiceDiscovery()
    {
        browser.ServiceAdded += (sender, args) =>
        {
            var connectableAddress = TryConnect(args.Announcement.Addresses.ToArray(), args.Announcement.Port);
            if (connectableAddress is null) return;

            var announcement = new ServiceAnnouncement
            {
                Address = connectableAddress,
                Port = args.Announcement.Port
            };
            ServiceAdded?.Invoke(sender, new ServiceAnnouncementEventArgs(announcement));
        };
        
        browser.ServiceRemoved += (sender, args) =>
        {
            var announcement = new ServiceAnnouncement
            {
                Address = args.Announcement.Addresses[0],
                Port = args.Announcement.Port,
            };
            ServiceRemoved?.Invoke(sender, new ServiceAnnouncementEventArgs(announcement));
        };
    }
    
    public void StartBrowse(string type)
    {
        browser.StartBrowse(type);
    }

    public void StopBrowse()
    {
        browser.StopBrowse();
    }

    private static IPAddress? TryConnect(IPAddress[] addresses, int port, int timeout = 2000)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(addresses.ToArray(), port, null, null);
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