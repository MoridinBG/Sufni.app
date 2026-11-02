using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Sufni.App.Infrastructure;

internal sealed class SocketServiceDiscoveryEndpointCache
{
    private readonly Dictionary<ServiceEndpointKey, ServiceAnnouncement> announcements = [];

    public void Add(IEnumerable<IPAddress> announcedAddresses, ServiceAnnouncement announcement)
    {
        if (!string.IsNullOrEmpty(announcement.InstanceName))
        {
            foreach (var key in announcements.Keys.Where(key => key.InstanceName == announcement.InstanceName && key.Port == announcement.Port).ToArray())
                announcements.Remove(key);
        }
        foreach (var address in announcedAddresses)
            announcements[new(announcement.InstanceName, address.ToString(), announcement.Port)] = announcement;
    }

    public bool TryRemove(string? instanceName, IEnumerable<IPAddress> announcedAddresses, ushort port,
        out ServiceAnnouncement announcement)
    {
        foreach (var address in announcedAddresses)
        {
            if (!announcements.TryGetValue(new(instanceName, address.ToString(), port), out var found)) continue;
            foreach (var key in announcements.Where(pair => ReferenceEquals(pair.Value, found)).Select(pair => pair.Key).ToArray())
                announcements.Remove(key);
            announcement = found;
            return true;
        }
        announcement = null!;
        return false;
    }

    public IReadOnlyList<ServiceAnnouncement> Drain()
    {
        var removed = announcements.Values.Distinct().ToArray();
        announcements.Clear();
        return removed;
    }

    private readonly record struct ServiceEndpointKey(string? InstanceName, string Address, ushort Port);
}
