using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        var credentials = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secureStorage = Substitute.For<ISecureStorage>();
        secureStorage.GetStringAsync("jwt_secret").Returns(credentials.Task);
        var server = new SynchronizationServerService(
            Substitute.For<IInboundSyncCoordinator>(),
            Substitute.For<IPairedDeviceRepository>(),
            Substitute.For<ISessionRepository>(),
            Substitute.For<ISessionSyncApplier>(),
            Substitute.For<IRecordedSessionSourceRepository>(),
            Substitute.For<IRecordedSessionSourceSyncQuery>(),
            secureStorage,
            Substitute.For<ISessionBlobSwapRequestStore>(),
            Substitute.For<IAppPaths>(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

        var start = server.StartAsync();
        Assert.Same(start, server.StartAsync());
        credentials.SetException(new IOException("credential storage unavailable"));
        var failure = Assert.IsType<ServerStartResult.Failed>(
            await start.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ServerStartFailureReason.Start, failure.Reason);
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

    [Fact]
    public void CreateServiceInstanceNames_StartsWithDefaultName_AndProvidesConflictFallbacks()
    {
        Assert.Equal(
            ["s1", "s1-2", "s1-3", "s1-4", "s1-5"],
            SynchronizationServerService.CreateServiceInstanceNames().ToList());
    }

    [Theory]
    [InlineData(InboundSyncWriteResult.Applied, StatusCodes.Status204NoContent)]
    [InlineData(InboundSyncWriteResult.Failed, StatusCodes.Status500InternalServerError)]
    public async Task ApplySynchronizationPushAsync_AwaitsCoordinatorOutcomeBeforeAcknowledging(
        InboundSyncWriteResult outcome, int status)
    {
        var data = new SynchronizationData();
        var coordinator = Substitute.For<IInboundSyncCoordinator>();
        var completion = new TaskCompletionSource<InboundSyncWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.ApplyPushAsync(data).Returns(completion.Task);
        var apply = SynchronizationServerService.ApplySynchronizationPushAsync(data, coordinator);
        Assert.False(apply.IsCompleted);
        completion.SetResult(outcome);
        await AssertStatusCodeAsync(await apply.AwaitBoundedAsync(TimeSpan.FromSeconds(5)), status);
    }

    [Theory]
    [InlineData(SessionDataApplyResult.Applied, StatusCodes.Status204NoContent, true)]
    [InlineData(SessionDataApplyResult.Superseded, StatusCodes.Status204NoContent, true)]
    [InlineData(SessionDataApplyResult.InvalidPayload, StatusCodes.Status400BadRequest, false)]
    [InlineData(SessionDataApplyResult.Missing, StatusCodes.Status404NotFound, false)]
    [InlineData(SessionDataApplyResult.Failed, StatusCodes.Status404NotFound, false)]
    public async Task ApplySessionDataPatchAsync_MapsApplicationOutcome_AndReportsAcceptedArrival(
        SessionDataApplyResult outcome, int status, bool accepted)
    {
        var id = Guid.NewGuid();
        var payload = new SessionBlobPayload("target", [1, 2, 3]);
        var applier = Substitute.For<ISessionSyncApplier>();
        applier.ApplyUploadedAsync(id, payload).Returns(outcome);
        var arrivals = new List<Guid>();

        var result = await SynchronizationServerService.ApplySessionDataPatchAsync(id, payload, applier, arrivals.Add);

        await AssertStatusCodeAsync(result, status);
        Assert.Equal(accepted ? [id] : Array.Empty<Guid>(), arrivals);
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
