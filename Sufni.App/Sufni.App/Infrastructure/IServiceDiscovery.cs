using System;
using System.Collections.Generic;
using System.Net;

namespace Sufni.App.Infrastructure;

public class ServiceAnnouncement
{
    public ServiceAnnouncement()
    {
    }

    public ServiceAnnouncement(IPAddress address, ushort port)
        : this(address, port, null, null)
    {
    }

    public ServiceAnnouncement(
        IPAddress address,
        ushort port,
        string? instanceName,
        IReadOnlyDictionary<string, string>? txtRecords)
    {
        Address = address;
        Port = port;
        InstanceName = instanceName;
        TxtRecords = txtRecords ?? new Dictionary<string, string>();
    }

    public ushort Port { get; internal set; }
    public IPAddress Address { get; internal set; } = null!;
    public string? InstanceName { get; internal set; }
    public IReadOnlyDictionary<string, string> TxtRecords { get; internal set; } = new Dictionary<string, string>();
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
