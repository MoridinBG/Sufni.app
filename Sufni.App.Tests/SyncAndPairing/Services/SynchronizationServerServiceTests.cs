using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Tests.SyncAndPairing.Services;

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

    [Fact]
    public async Task ApplySessionDataPatchAsync_FillPersistsThroughTelemetryWriter_AndRaisesSessionDataArrived()
    {
        var sessionId = Guid.NewGuid();
        var payload = new SessionBlobPayload("fingerprint-a", [1, 2, 3]);
        var sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
        var swapRequestStore = Substitute.For<ISessionBlobSwapRequestStore>();
        var arrivedSessionIds = new List<Guid>();
        swapRequestStore.GetTargetFingerprintAsync(sessionId).Returns((string?)null);

        var result = await SynchronizationServerService.ApplySessionDataPatchAsync(
            sessionId,
            payload,
            sessionTelemetryWriter,
            swapRequestStore,
            arrivedSessionIds.Add);

        await sessionTelemetryWriter.Received(1).PatchSessionPsstAsync(
            sessionId,
            payload.Data,
            payload.Fingerprint);
        await sessionTelemetryWriter.DidNotReceive().SwapSessionPsstAsync(
            Arg.Any<Guid>(),
            Arg.Any<byte[]>(),
            Arg.Any<string?>());
        await swapRequestStore.DidNotReceive().ClearAsync(Arg.Any<Guid>());
        Assert.Equal([sessionId], arrivedSessionIds);
        await AssertStatusCodeAsync(result, StatusCodes.Status204NoContent);
    }

    private static async Task AssertStatusCodeAsync(IResult result, int expectedStatusCode)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider(),
        };

        await result.ExecuteAsync(context);

        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
    }
}
