using System;
using System.Collections.Generic;
using System.Net;

namespace Sufni.App.Infrastructure;

public class ServiceAnnouncement
{
    public ServiceAnnouncement(IPAddress address, ushort port)
        : this(new ServiceEndpoint([address], port), null, null) { }

    public ServiceAnnouncement(IPAddress address, ushort port, string? instanceName,
        IReadOnlyDictionary<string, string>? txtRecords)
        : this(new ServiceEndpoint([address], port), instanceName, txtRecords) { }

    public ServiceAnnouncement(ServiceEndpoint endpoint, string? instanceName,
        IReadOnlyDictionary<string, string>? txtRecords)
    {
        Endpoint = endpoint;
        InstanceName = instanceName;
        TxtRecords = txtRecords ?? new Dictionary<string, string>();
    }

    public ServiceEndpoint Endpoint { get; }
    public ushort Port => (ushort)Endpoint.Port;
    public IPAddress Address => IPAddress.Parse(Endpoint.Addresses[0]);
    public string? InstanceName { get; }
    public IReadOnlyDictionary<string, string> TxtRecords { get; }

}

public class ServiceAnnouncementEventArgs : EventArgs
{
    public ServiceAnnouncementEventArgs(ServiceAnnouncement announcement)
    {
        Announcement = announcement;
    }

    public ServiceAnnouncement Announcement { private set; get; }
}

// Discovery adapter for platform-specific service browsing. Shared code only
// observes announcements; Bonjour/socket details stay behind implementations.
public interface IServiceDiscovery
{
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    public void StartBrowse(string type);
    public void StopBrowse();
}
