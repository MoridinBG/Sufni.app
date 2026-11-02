using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;

namespace Sufni.App.Infrastructure;

/// <summary>Resolved address candidates for one service port, captured without opening a connection.</summary>
public sealed record ServiceEndpoint
{
    public ImmutableArray<string> Addresses { get; }
    public int Port { get; }
    public string Host => Addresses[0].Contains(':') ? $"[{Addresses[0]}]" : Addresses[0];

    public ServiceEndpoint(string address, int port) : this([IPAddress.Parse(address)], port) { }

    public ServiceEndpoint(IEnumerable<IPAddress> addresses, int port)
    {
        Addresses = addresses.Select(address => (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString())
            .Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (Addresses.IsEmpty) throw new ArgumentException("A service endpoint requires an address.", nameof(addresses));
        if (port is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port;
    }

    public IPAddress[] GetAddresses() => Addresses.Select(IPAddress.Parse).ToArray();
    public bool Equals(ServiceEndpoint? other) => other is not null && Port == other.Port && Addresses.SequenceEqual(other.Addresses);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Port);
        foreach (var address in Addresses) hash.Add(address, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
    public override string ToString() => $"{Host}:{Port}";
}
