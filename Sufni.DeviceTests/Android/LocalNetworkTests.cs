using Sufni.App.Android;
using Sufni.App.Infrastructure;

namespace Sufni.DeviceTests;

[TestClass]
public sealed class LocalNetworkTests
{
    [TestMethod]
    public async Task GrantedLocalNetworkAccessReceivesLanAnnouncement()
    {
        Assert.IsTrue(OperatingSystem.IsAndroidVersionAtLeast(37), "Use an Android 17 or newer device.");
        var permission = new AndroidLocalNetworkPermission();
        Assert.IsTrue(permission.IsGranted);

        var discovery = new AndroidServiceDiscovery(
            new SocketServiceDiscovery(ServiceDiscoveryResolution.AnnouncedAddresses),
            new AndroidMulticastLockFactory(), permission);
        var found = new TaskCompletionSource<ServiceAnnouncement>(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.ServiceAdded += (_, e) => found.TrySetResult(e.Announcement);
        try
        {
            discovery.StartBrowse("_sufnismoke._tcp");
            var service = await found.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsNotEmpty(service.Endpoint.Addresses);
        }
        finally
        {
            discovery.StopBrowse();
        }
    }
}
