using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Models;
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
            Substitute.For<ISyncDataStore>(),
            Substitute.For<IPairedDeviceRepository>(),
            Substitute.For<ISessionRepository>(),
            Substitute.For<ISessionTelemetryWriter>(),
            Substitute.For<IRecordedSessionSourceRepository>(),
            Substitute.For<IRecordedSessionSourceSyncQuery>(),
            Substitute.For<IAppPreferences>(),
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

    [Fact]
    public async Task ApplySynchronizationPushAsync_MergesCoreAndPreferencesBeforePublishing()
    {
        var calls = new List<string>();
        var data = new SynchronizationData();
        var syncDataStore = Substitute.For<ISyncDataStore>();
        var appPreferences = Substitute.For<IAppPreferences>();
        syncDataStore.MergeAllAsync(data).Returns(_ =>
        {
            calls.Add("core");
            return Task.CompletedTask;
        });
        appPreferences.ApplySyncDataAsync(data.AppPreferences).Returns(_ =>
        {
            calls.Add("preferences");
            return Task.CompletedTask;
        });

        var result = await SynchronizationServerService.ApplySynchronizationPushAsync(
            data,
            syncDataStore,
            appPreferences,
            _ => calls.Add("publish"));

        Assert.Equal(["core", "preferences", "publish"], calls);
        await AssertStatusCodeAsync(result, StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task ApplySessionDataPatchAsync_FillPersistsThroughTelemetryWriter_AndRaisesSessionDataArrived()
    {
        var sessionId = Guid.NewGuid();
        var payload = new SessionBlobPayload("fingerprint-a", [1, 2, 3]);
        var sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
        var swapRequestStore = Substitute.For<ISessionBlobSwapRequestStore>();
        var arrivedSessionIds = new List<Guid>();
        swapRequestStore.GetRequestAsync(sessionId).Returns((SessionBlobSwap?)null);

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

    [Fact]
    public async Task ApplySessionDataPatchAsync_MatchingSwapAppliesStoredGenerationAndClearsRequest()
    {
        var sessionId = Guid.NewGuid();
        var payload = new SessionBlobPayload("target", [1, 2, 3]);
        var generation = new SessionProcessedGeneration(
            DurationSeconds: 80,
            DistanceMeters: 20,
            AscentMeters: 8,
            DescentMeters: 3,
            FullTrackId: Guid.NewGuid(),
            GpsOffsetSeconds: 1.25,
            Track: [new Sufni.App.MapsAndTracks.Models.TrackPoint(100, 1, 2, 3)]);
        var sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
        var swapRequestStore = Substitute.For<ISessionBlobSwapRequestStore>();
        swapRequestStore.GetRequestAsync(sessionId)
            .Returns(new SessionBlobSwap(sessionId, payload.Fingerprint!, generation));
        var arrivedSessionIds = new List<Guid>();

        var result = await SynchronizationServerService.ApplySessionDataPatchAsync(
            sessionId,
            payload,
            sessionTelemetryWriter,
            swapRequestStore,
            arrivedSessionIds.Add);

        await sessionTelemetryWriter.Received(1).SwapSessionPsstAsync(
            sessionId,
            payload.Data,
            payload.Fingerprint,
            generation);
        await sessionTelemetryWriter.DidNotReceive().SwapSessionPsstAsync(
            Arg.Any<Guid>(),
            Arg.Any<byte[]>(),
            Arg.Any<string?>());
        await swapRequestStore.Received(1).ClearAsync(sessionId);
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
