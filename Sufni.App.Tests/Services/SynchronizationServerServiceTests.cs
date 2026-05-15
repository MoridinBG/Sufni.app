using System.Net;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class SynchronizationServerServiceTests
{
    [Fact]
    public void SelectAdvertisedAddresses_FiltersLinkLocalAndLoopbackAddresses_AndPrefersIPv4()
    {
        var globalIpv6 = IPAddress.Parse("2001:db8::7");
        var ipv4 = IPAddress.Parse("192.168.2.7");
        var selected = SynchronizationServerService.SelectAdvertisedAddresses(
        [
            IPAddress.Parse("fe80::64:714a:310e:fb4a"),
            globalIpv6,
            IPAddress.Loopback,
            IPAddress.IPv6Loopback,
            IPAddress.Parse("169.254.254.226"),
            ipv4,
        ]);

        Assert.Equal([ipv4, globalIpv6], selected);
    }

    [Fact]
    public void CreateServiceInstanceNames_StartsWithDefaultName_AndProvidesConflictFallbacks()
    {
        Assert.Equal(
            ["s1", "s1-2", "s1-3", "s1-4", "s1-5"],
            SynchronizationServerService.CreateServiceInstanceNames().ToList());
    }
}
