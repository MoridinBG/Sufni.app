using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Sync;
using Sufni.App.Extensibility.Sync;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Models;
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
    public async Task ApplySynchronizationPushAsync_PreparesExtensionsBeforeCoreMerge()
    {
        var calls = new List<string>();
        var data = CreateSynchronizationDataWithExtensionBatch();
        var syncDataStore = Substitute.For<ISyncDataStore>();
        var appPreferences = Substitute.For<IAppPreferences>();
        var participant = CreateExtensionParticipant();
        participant.PrepareBatchAsync(Arg.Any<ExtensionSyncEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("prepare");
                return new ExtensionSyncPrepareResult.Prepared(new PreparedBatch());
            });
        participant.ApplyPreparedBatchAsync(Arg.Any<IExtensionSyncPreparedBatch>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("extension");
                return new ExtensionSyncApplyResult.Applied([]);
            });
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
            new ExtensionSyncService([participant]),
            _ => calls.Add("publish"));

        Assert.Equal(["prepare", "core", "preferences", "extension", "publish"], calls);
        await AssertStatusCodeAsync(result, StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task ApplySynchronizationPushAsync_DoesNotMutateCore_WhenExtensionPreparationFails()
    {
        var data = CreateSynchronizationDataWithExtensionBatch();
        var syncDataStore = Substitute.For<ISyncDataStore>();
        var appPreferences = Substitute.For<IAppPreferences>();
        var participant = CreateExtensionParticipant();
        participant.PrepareBatchAsync(Arg.Any<ExtensionSyncEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSyncPrepareResult.Failed("extension payload invalid"));
        var published = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SynchronizationServerService.ApplySynchronizationPushAsync(
                data,
                syncDataStore,
                appPreferences,
                new ExtensionSyncService([participant]),
                _ => published++));

        await syncDataStore.DidNotReceive().MergeAllAsync(Arg.Any<SynchronizationData>());
        await appPreferences.DidNotReceive().ApplySyncDataAsync(Arg.Any<AppPreferencesSyncData?>());
        Assert.Equal(0, published);
    }

    [Fact]
    public async Task ApplySynchronizationPushAsync_PublishesCoreAndReturnsFailure_WhenExtensionApplyFails()
    {
        var data = CreateSynchronizationDataWithExtensionBatch();
        var syncDataStore = Substitute.For<ISyncDataStore>();
        var appPreferences = Substitute.For<IAppPreferences>();
        var participant = CreateExtensionParticipant();
        participant.PrepareBatchAsync(Arg.Any<ExtensionSyncEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSyncPrepareResult.Prepared(new PreparedBatch()));
        participant.ApplyPreparedBatchAsync(Arg.Any<IExtensionSyncPreparedBatch>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionSyncApplyResult.Failed("extension failed", []));
        var published = 0;

        var result = await SynchronizationServerService.ApplySynchronizationPushAsync(
            data,
            syncDataStore,
            appPreferences,
            new ExtensionSyncService([participant]),
            _ => published++);

        await syncDataStore.Received(1).MergeAllAsync(data);
        await appPreferences.Received(1).ApplySyncDataAsync(data.AppPreferences);
        Assert.Equal(1, published);
        await AssertStatusCodeAsync(result, StatusCodes.Status500InternalServerError);
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
            Track: [new Sufni.App.ExtensionHost.Contracts.Models.TrackPoint(100, 1, 2, 3)]);
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

    private static SynchronizationData CreateSynchronizationDataWithExtensionBatch() => new()
    {
        ExtensionBatches =
        [
            new ExtensionSyncEnvelope(
                "test",
                SchemaVersion: 1,
                ContentType: "application/test",
                Payload: [1, 2, 3])
        ]
    };

    private static IExtensionSyncParticipant CreateExtensionParticipant()
    {
        var participant = Substitute.For<IExtensionSyncParticipant>();
        participant.ExtensionId.Returns("test");
        return participant;
    }

    private sealed record PreparedBatch : IExtensionSyncPreparedBatch;

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
