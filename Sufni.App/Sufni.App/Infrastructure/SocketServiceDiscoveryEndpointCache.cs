using System;
using System.Collections.Generic;
using System.Net;

namespace Sufni.App.Infrastructure;

internal sealed class SocketServiceDiscoveryEndpointCache
{
    private readonly Dictionary<ServiceEndpointKey, IPAddress> emittedAddresses = [];

    public void Add(IEnumerable<IPAddress> announcedAddresses, ushort port, IPAddress emittedAddress)
    {
        Add(instanceName: null, announcedAddresses, port, emittedAddress);
    }

    public void Add(string? instanceName, IEnumerable<IPAddress> announcedAddresses, ushort port, IPAddress emittedAddress)
    {
        foreach (var address in announcedAddresses)
        {
            emittedAddresses[new ServiceEndpointKey(instanceName, address, port)] = emittedAddress;
        }
    }

    public bool TryRemove(IEnumerable<IPAddress> announcedAddresses, ushort port, out IPAddress emittedAddress)
    {
        return TryRemove(instanceName: null, announcedAddresses, port, out emittedAddress);
    }

    public bool TryRemove(string? instanceName, IEnumerable<IPAddress> announcedAddresses, ushort port, out IPAddress emittedAddress)
    {
        IPAddress? removedAddress = null;
        foreach (var address in announcedAddresses)
        {
            var key = new ServiceEndpointKey(instanceName, address, port);
            if (removedAddress is null && emittedAddresses.TryGetValue(key, out var cachedAddress))
            {
                removedAddress = cachedAddress;
            }

            emittedAddresses.Remove(key);
        }

        emittedAddress = removedAddress!;
        return removedAddress is not null;
    }

    private readonly record struct ServiceEndpointKey(string? InstanceName, IPAddress Address, ushort Port);
}
