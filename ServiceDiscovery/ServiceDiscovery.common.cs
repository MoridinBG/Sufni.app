using System.Net;

namespace ServiceDiscovery;

public class ServiceAnnouncement
{
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