using System;
using System.Collections.Generic;
using System.Net;

namespace Sufni.App.Services;

internal sealed class SocketServiceDiscoveryEndpointCache
{
    private readonly Dictionary<ServiceEndpointKey, IPAddress> emittedAddresses = [];

    public void Add(IEnumerable<IPAddress> announcedAddresses, ushort port, IPAddress emittedAddress)
    {
        foreach (var address in announcedAddresses)
        {
            emittedAddresses[new ServiceEndpointKey(address, port)] = emittedAddress;
        }
    }

    public bool TryRemove(IEnumerable<IPAddress> announcedAddresses, ushort port, out IPAddress emittedAddress)
    {
        IPAddress? removedAddress = null;
        foreach (var address in announcedAddresses)
        {
            var key = new ServiceEndpointKey(address, port);
            if (removedAddress is null && emittedAddresses.TryGetValue(key, out var cachedAddress))
            {
                removedAddress = cachedAddress;
            }

            emittedAddresses.Remove(key);
        }

        emittedAddress = removedAddress!;
        return removedAddress is not null;
    }

    private readonly record struct ServiceEndpointKey(IPAddress Address, ushort Port);
}
