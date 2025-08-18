using System.Net;
using CoreFoundation;
using Network;

namespace ServiceDiscovery;

public class ServiceDiscovery : IServiceDiscovery
{
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded;
    public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved;

    private readonly DispatchQueue dispatchQueue = new("com.sghctoma.sufni-bridge.serviceDiscovery");
    private NWBrowser browser;
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
        if (result is null) return;

        // We need to initiate a connection to obtain the IP address and port,
        // because there is no other way to resolve the address:
        // https://developer.apple.com/forums/thread/122638?answerId=382318022#382318022
        // Please note that we satisfy the resolve-only-when-need-to-connect
        // recommendation, because we will immediately ask for the SST file list.
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

                    if (currentIpAddress is null || currentPort is null) return;

                    ServiceAdded?.Invoke(this, new ServiceAnnouncementEventArgs(new ServiceAnnouncement()
                    {
                        Address = currentIpAddress,
                        Port = currentPort.Value,
                    }));
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

        ServiceRemoved?.Invoke(this, new ServiceAnnouncementEventArgs(new ServiceAnnouncement()
        {
            Address = currentIpAddress,
            Port = currentPort.Value,
        }));
    }

    public ServiceDiscovery()
    {
        var browserDescriptor = NWBrowserDescriptor.CreateBonjourService(type, "local.");
        browser = new NWBrowser(browserDescriptor, parameters);
        browser.SetDispatchQueue(dispatchQueue);

        browser.CompleteChangesDelegate = changes =>
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
    }

    public void StartBrowse(string type)
    {
        browser.Start();
    }

    public void StopBrowse()
    {
        browser.Cancel();
    }
}