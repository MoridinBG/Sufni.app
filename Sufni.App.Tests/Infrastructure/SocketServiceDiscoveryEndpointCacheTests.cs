using System.Net;

using Sufni.App.Infrastructure;
namespace Sufni.App.Tests.Infrastructure;

public class SocketServiceDiscoveryEndpointCacheTests
{
    [Fact]
    public void Drain_ReturnsEachEmittedEndpointOnce_AndClearsRemovalState()
    {
        var firstAddress = IPAddress.Parse("192.168.0.10");
        var resolvedAddress = IPAddress.Parse("192.168.0.11");
        var cache = new SocketServiceDiscoveryEndpointCache();
        cache.Add("daq-a", [firstAddress, resolvedAddress], 5575, resolvedAddress);
        cache.Add("daq-b", [firstAddress], 5575, firstAddress);

        var removed = cache.Drain();

        Assert.Equal(2, removed.Count);
        Assert.Contains(removed, item => item.InstanceName == "daq-a" && item.Address.Equals(resolvedAddress) && item.Port == 5575);
        Assert.Contains(removed, item => item.InstanceName == "daq-b" && item.Address.Equals(firstAddress) && item.Port == 5575);
        Assert.Empty(cache.Drain());
        Assert.False(cache.TryRemove("daq-a", [firstAddress, resolvedAddress], 5575, out _));
        cache.Add("daq-a", [firstAddress], 5575, firstAddress);
        Assert.Equal(firstAddress, Assert.Single(cache.Drain()).Address);
    }

    [Fact]
    public void Remove_ReturnsEmittedConnectableAddress_WhenAnnouncedFirstAddressDiffers()
    {
        var announcedFirst = IPAddress.Parse("192.168.0.10");
        var connectable = IPAddress.Parse("192.168.0.11");
        var cache = new SocketServiceDiscoveryEndpointCache();

        cache.Add(new[] { announcedFirst, connectable }, 5575, connectable);

        var removed = cache.TryRemove(new[] { announcedFirst, connectable }, 5575, out var emittedAddress);

        Assert.True(removed);
        Assert.Equal(connectable, emittedAddress);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_WhenEndpointWasNotCached()
    {
        var announcedFirst = IPAddress.Parse("192.168.0.10");
        var announcedSecond = IPAddress.Parse("192.168.0.11");
        var cache = new SocketServiceDiscoveryEndpointCache();

        var removed = cache.TryRemove(new[] { announcedFirst, announcedSecond }, 5575, out _);

        Assert.False(removed);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_WhenAnnouncementHasNoAddresses()
    {
        var cache = new SocketServiceDiscoveryEndpointCache();

        var removed = cache.TryRemove(Array.Empty<IPAddress>(), 5575, out _);

        Assert.False(removed);
    }

    [Fact]
    public void TryRemove_UsesInstanceName_WhenProvided()
    {
        var announcedAddress = IPAddress.Parse("192.168.0.10");
        var firstConnectable = IPAddress.Parse("192.168.0.11");
        var secondConnectable = IPAddress.Parse("192.168.0.12");
        var cache = new SocketServiceDiscoveryEndpointCache();

        cache.Add("daq-a", new[] { announcedAddress }, 5575, firstConnectable);
        cache.Add("daq-b", new[] { announcedAddress }, 5575, secondConnectable);

        var removedFirst = cache.TryRemove("daq-a", new[] { announcedAddress }, 5575, out var firstEmittedAddress);
        var removedSecond = cache.TryRemove("daq-b", new[] { announcedAddress }, 5575, out var secondEmittedAddress);

        Assert.True(removedFirst);
        Assert.True(removedSecond);
        Assert.Equal(firstConnectable, firstEmittedAddress);
        Assert.Equal(secondConnectable, secondEmittedAddress);
    }
}
