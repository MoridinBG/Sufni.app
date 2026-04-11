using System;
using System.Net;

namespace Sufni.App.Services;

public class ServiceAnnouncement
{
    public ServiceAnnouncement()
    {
    }

    public ServiceAnnouncement(IPAddress address, ushort port)
    {
        Address = address;
        Port = port;
    }

    public ushort Port { get; internal set; }
    public IPAddress Address { get; internal set; } = null!;
}

public class ServiceAnnouncementEventArgs : EventArgs
{
    public ServiceAnnouncementEventArgs(ServiceAnnouncement announcement)
    {
        Announcement = announcement;
    }

    public ServiceAnnouncement Announcement { private set; get; }
}

public interface IServiceDiscovery
{
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    public void StartBrowse(string type);
    public void StopBrowse();
}