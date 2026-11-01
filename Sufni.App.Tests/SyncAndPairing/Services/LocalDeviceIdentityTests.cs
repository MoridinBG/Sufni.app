using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.SyncAndPairing.Services;

public class LocalDeviceIdentityTests
{
    [Fact]
    public async Task ExistingPairingIdentity_IsPreserved()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetStringAsync("DeviceId", "existing-paired-device");

        Assert.Equal("existing-paired-device", await new LocalDeviceIdentity(storage).GetIdAsync());
    }

    [Fact]
    public async Task NewIdentity_SurvivesAServiceRestart()
    {
        var storage = new InMemorySecureStorage();
        var first = await new LocalDeviceIdentity(storage).GetIdAsync();

        Assert.True(Guid.TryParse(first, out _));
        Assert.Equal(first, await new LocalDeviceIdentity(storage).GetIdAsync());
    }

    [Fact]
    public async Task ConcurrentCallers_ShareIdentityAndWaitForItsPersistence()
    {
        var storage = Substitute.For<ISecureStorage>();
        var read = new TaskCompletionSource<string?>();
        var persisted = new TaskCompletionSource();
        string? written = null;
        storage.GetStringAsync("DeviceId").Returns(read.Task);
        storage.SetStringAsync("DeviceId", Arg.Any<string>()).Returns(call =>
        {
            written = call.ArgAt<string>(1);
            return persisted.Task;
        });
        var identity = new LocalDeviceIdentity(storage);
        var first = identity.GetIdAsync();
        var second = identity.GetIdAsync();
        read.SetResult(null);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        persisted.SetResult();

        Assert.Equal(written, await first.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(written, await second.AwaitBoundedAsync(TimeSpan.FromSeconds(2)));
        await storage.Received(1).SetStringAsync("DeviceId", written);
    }
}
