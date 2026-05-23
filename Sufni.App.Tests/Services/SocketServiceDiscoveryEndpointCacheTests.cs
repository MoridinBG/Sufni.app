using System.Net;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class SocketServiceDiscoveryEndpointCacheTests
{
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
}
