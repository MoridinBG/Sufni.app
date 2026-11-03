using System.Net;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizationServerServiceTests
{
    [Fact]
    public async Task StartAsync_ReturnsFailureForCredentialInitialization_AndStopRemainsSafe()
    {
        var credentials = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secureStorage = Substitute.For<ISecureStorage>();
        var tokens = Substitute.For<ISynchronizationTokenService>();
        tokens.GetSigningKeyAsync().Returns(credentials.Task);
        var server = new SynchronizationServerService(
            Substitute.For<IInboundSyncCoordinator>(),
            tokens,
            Substitute.For<ISessionRepository>(),
            Substitute.For<IRecordedSessionSourceRepository>(),
            Substitute.For<IRecordedSessionSourceSyncQuery>(),
            secureStorage,
            Substitute.For<ISessionBlobSwapRequestStore>(),
            Substitute.For<IAppPaths>(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

        var start = server.StartAsync();
        var concurrentStart = server.StartAsync();
        Assert.False(concurrentStart.IsCompleted);
        credentials.SetException(new IOException("credential storage unavailable"));
        var failure = Assert.IsType<ServerStartResult.Failed>(
            await start.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ServerStartFailureReason.Start, failure.Reason);
        Assert.Equal(ServerStartFailureReason.Start, Assert.IsType<ServerStartResult.Failed>(
            await concurrentStart.AwaitBoundedAsync(TimeSpan.FromSeconds(5))).Reason);
        Assert.NotEmpty(failure.Details!);
        await server.StopAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }

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

}
