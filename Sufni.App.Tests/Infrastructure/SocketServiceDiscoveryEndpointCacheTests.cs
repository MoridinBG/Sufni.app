using System.Net;

using Sufni.App.Infrastructure;
namespace Sufni.App.Tests.Infrastructure;

public class SocketServiceDiscoveryEndpointCacheTests
{
    [Fact]
    public void UpdatedAnnouncement_RemovalReturnsCurrentCandidatesWithoutOldAliases()
    {
        var cache = new SocketServiceDiscoveryEndpointCache();
        var oldAddress = IPAddress.Parse("192.168.0.10");
        var currentAddress = IPAddress.Parse("192.168.0.11");
        cache.Add([oldAddress, currentAddress], new ServiceAnnouncement(new ServiceEndpoint([oldAddress, currentAddress], 1557), "daq", null));
        var current = new ServiceAnnouncement(currentAddress, 1557, "daq", null);
        cache.Add([currentAddress], current);

        Assert.False(cache.TryRemove("daq", [oldAddress], 1557, out _));
        Assert.True(cache.TryRemove("daq", [currentAddress], 1557, out var removed));
        Assert.Same(current, removed);
        Assert.Empty(cache.Drain());
    }

    [Fact]
    public void Drain_ReturnsEachEmittedEndpointOnce_AndClearsRemovalState()
    {
        var firstAddress = IPAddress.Parse("192.168.0.10");
        var resolvedAddress = IPAddress.Parse("192.168.0.11");
        var cache = new SocketServiceDiscoveryEndpointCache();
        cache.Add([firstAddress, resolvedAddress], new ServiceAnnouncement(new ServiceEndpoint([resolvedAddress, firstAddress], 5575), "daq-a", null));
        cache.Add([firstAddress], new ServiceAnnouncement(firstAddress, 5575, "daq-b", null));

        var removed = cache.Drain();

        Assert.Equal(2, removed.Count);
        Assert.Equal(2, removed.Single(item => item.InstanceName == "daq-a").Endpoint.Addresses.Length);
        Assert.Contains(removed, item => item.InstanceName == "daq-a" && item.Address.Equals(resolvedAddress) && item.Port == 5575);
        Assert.Contains(removed, item => item.InstanceName == "daq-b" && item.Address.Equals(firstAddress) && item.Port == 5575);
        Assert.Empty(cache.Drain());
        Assert.False(cache.TryRemove("daq-a", [firstAddress, resolvedAddress], 5575, out _));
        cache.Add([firstAddress], new ServiceAnnouncement(firstAddress, 5575, "daq-a", null));
        Assert.Equal(firstAddress, Assert.Single(cache.Drain()).Address);
    }

    [Fact]
    public void Remove_ReturnsEmittedConnectableAddress_WhenAnnouncedFirstAddressDiffers()
    {
        var announcedFirst = IPAddress.Parse("192.168.0.10");
        var connectable = IPAddress.Parse("192.168.0.11");
        var cache = new SocketServiceDiscoveryEndpointCache();

        cache.Add([announcedFirst, connectable], new ServiceAnnouncement(connectable, 5575));

        var removed = cache.TryRemove(null, new[] { announcedFirst, connectable }, 5575, out var emittedAddress);

        Assert.True(removed);
        Assert.Equal(connectable, emittedAddress.Address);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_WhenEndpointWasNotCached()
    {
        var announcedFirst = IPAddress.Parse("192.168.0.10");
        var announcedSecond = IPAddress.Parse("192.168.0.11");
        var cache = new SocketServiceDiscoveryEndpointCache();

        var removed = cache.TryRemove(null, new[] { announcedFirst, announcedSecond }, 5575, out _);

        Assert.False(removed);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_WhenAnnouncementHasNoAddresses()
    {
        var cache = new SocketServiceDiscoveryEndpointCache();

        var removed = cache.TryRemove(null, Array.Empty<IPAddress>(), 5575, out _);

        Assert.False(removed);
    }

    [Fact]
    public void TryRemove_UsesInstanceName_WhenProvided()
    {
        var announcedAddress = IPAddress.Parse("192.168.0.10");
        var firstConnectable = IPAddress.Parse("192.168.0.11");
        var secondConnectable = IPAddress.Parse("192.168.0.12");
        var cache = new SocketServiceDiscoveryEndpointCache();

        cache.Add([announcedAddress], new ServiceAnnouncement(firstConnectable, 5575, "daq-a", null));
        cache.Add([announcedAddress], new ServiceAnnouncement(secondConnectable, 5575, "daq-b", null));

        var removedFirst = cache.TryRemove("daq-a", new[] { announcedAddress }, 5575, out var firstEmittedAddress);
        var removedSecond = cache.TryRemove("daq-b", new[] { announcedAddress }, 5575, out var secondEmittedAddress);

        Assert.True(removedFirst);
        Assert.True(removedSecond);
        Assert.Equal(firstConnectable, firstEmittedAddress.Address);
        Assert.Equal(secondConnectable, secondEmittedAddress.Address);
    }
}
