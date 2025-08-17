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
            var announcement = new ServiceAnnouncement
            {
                Address = args.Announcement.Addresses[0],
                Port = args.Announcement.Port,
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
}