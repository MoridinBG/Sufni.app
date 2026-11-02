using Sufni.App.Tests.TestSupport.Async;
using NSubstitute;
using Sufni.App.Shared.Models;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Coordinators;
using System.Reactive.Linq;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sync;
using Sufni.App.Tests.TestSupport.Persistence;
using SQLite;
namespace Sufni.App.Tests.SyncAndPairing.Coordinators;

public class SyncCoordinatorClientRunTests : IDisposable
{
    private readonly SyncTestServerHarness harness = new();

    private ISyncDataStore syncDataStore => harness.SyncDataStore;
    private ISessionRepository sessionRepository => harness.SessionRepository;
    private IRecordedSessionSourceRepository recordedSessionSourceRepository => harness.RecordedSessionSourceRepository;
    private RecordedSessionSourceStore sourceStore => harness.SourceStore;
    private IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery => harness.RecordedSessionSourceSyncQuery;
    private ISynchronizationPeer httpApiService => harness.HttpApiService;

    private SyncCoordinator CreateCoordinator() =>
        harness.CreateCoordinator();

    [Fact]
    public async Task SyncAll_ChangedHubIncarnationStartsBaseline_AndPushesExistingLocalFacts()
    {
        using var file = new TempDatabase();
        await using var database = new TestPersistenceHarness(file.DatabasePath);
        var board = new Board(Guid.NewGuid(), null);
        await database.PutAsync(board);
        await database.UpdateLastPushSequenceAsync(SyncTestServerHarness.Peer, 1);
        await database.UpdateLastPullSequenceAsync(SyncTestServerHarness.Peer, 50);
        var resetPeer = SyncTestServerHarness.Peer with { DatabaseIncarnation = "reset-database" };
        var announcements = new List<SynchronizationRunStart>();
        httpApiService.BeginRunAsync(Arg.Any<SynchronizationRunStart>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            announcements.Add(call.Arg<SynchronizationRunStart>());
            return resetPeer;
        });
        var requests = new List<string>();
        httpApiService.PullSyncAsync(0, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            requests.Add("pull");
            return new SynchronizationData();
        });
        SynchronizationData? pushed = null;
        httpApiService.PushSyncAsync(Arg.Any<SynchronizationData>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            requests.Add("push");
            pushed = call.Arg<SynchronizationData>();
            return Task.CompletedTask;
        });

        Assert.IsType<SynchronizationRunResult.Completed>(await harness.CreateCoordinator(database.SyncDataStore)
            .RunSyncAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["pull", "push"], requests);
        Assert.Equal(2, announcements.Count);
        Assert.Equal(announcements[0].RunId, announcements[1].RunId);
        Assert.Equal(SynchronizationPhase.PushingLocalChanges, announcements[0].Phases[0]);
        Assert.Equal(SynchronizationPhase.PullingRemoteChanges, announcements[1].Phases[0]);
        Assert.Equal(board.Id, Assert.Single(pushed!.Boards).Id);
        Assert.Equal(1, await database.GetLastPushSequenceAsync(resetPeer));
        Assert.Equal(0, await database.GetLastPullSequenceAsync(resetPeer));
        Assert.Equal(50, await database.GetLastPullSequenceAsync(SyncTestServerHarness.Peer));
    }

    [Fact]
    public async Task SyncAll_OneUploadGetsConflict_OtherItemsComplete_AndPendingItemRetriesAfterRestart()
    {
        using var file = new TempDatabase();
        var database = new TestPersistenceHarness(file.DatabasePath);
        await database.PutAsync(new Board(Guid.NewGuid(), null));
        var bound = await database.GetSequenceUpperBoundAsync(SynchronizationDirection.Push);
        var rejectedId = Guid.NewGuid();
        var acceptedId = Guid.NewGuid();
        var uploads = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var retry = false;
        sessionRepository.GetSessionRawPsstWithFingerprintAsync(Arg.Any<Guid>())
            .Returns(Task.FromResult<(byte[] Data, string? Fingerprint)?>(([1, 2, 3], "target")));
        Task<System.Net.Http.HttpResponseMessage> Send(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == SynchronizationProtocol.EndpointPairRefresh)
                return Task.FromResult(SyncTestServerHarness.Json(new TokenResponse(
                    SyncTestServerHarness.CreateAccessToken(harness.Time.GetUtcNow().AddMinutes(10)), "refresh-2", SyncTestServerHarness.Peer.DatabaseIncarnation)));
            if (path == SynchronizationProtocol.EndpointRunStart)
                return Task.FromResult(SyncTestServerHarness.Json(new SynchronizationRunStarted(SyncTestServerHarness.Peer.DatabaseIncarnation)));
            if (path == SynchronizationProtocol.EndpointRunEnd)
                return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
            if (path == SynchronizationProtocol.EndpointSyncPull)
                return Task.FromResult(SyncTestServerHarness.Json(new SynchronizationData()));
            if (path == SynchronizationProtocol.EndpointSyncPush)
                return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
            if (path == SynchronizationProtocol.EndpointSessionIncomplete)
                return Task.FromResult(SyncTestServerHarness.Json(retry ? new[] { rejectedId } : [rejectedId, acceptedId]));
            if (path == SynchronizationProtocol.EndpointSessionSourceIncomplete)
                return Task.FromResult(SyncTestServerHarness.Json(Array.Empty<Guid>()));
            var id = Guid.Parse(path[SynchronizationProtocol.EndpointSessionData.Length..]);
            uploads.Add(id);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(id == rejectedId && !retry
                ? System.Net.HttpStatusCode.Conflict : System.Net.HttpStatusCode.NoContent));
        }

        await using var firstApi = harness.CreateHttpApiService(Send);
        var first = await harness.CreateCoordinator(database.SyncDataStore, await firstApi.GetPeerAsync(new PeerEndpoint("https://sync.example.test")))
            .RunSyncAsync( cancellationToken: TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<SynchronizationRunResult.IncompleteData>(first);
        Assert.Equal(new SynchronizationPendingTransfer(rejectedId, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Push),
            Assert.Single(incomplete.PendingTransfers));
        Assert.Contains(acceptedId, uploads);
        Assert.Equal(bound, await database.GetLastPushSequenceAsync(SyncTestServerHarness.Peer));

        retry = true;
        var reopened = new TestPersistenceHarness(file.DatabasePath);
        await using var secondApi = harness.CreateHttpApiService(Send);
        var second = await harness.CreateCoordinator(reopened.SyncDataStore, await secondApi.GetPeerAsync(new PeerEndpoint("https://sync.example.test")))
            .RunSyncAsync( cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<SynchronizationRunResult.Completed>(second);
        Assert.Equal(2, uploads.Count(id => id == rejectedId));
        Assert.Equal(1, uploads.Count(id => id == acceptedId));
    }

    [Fact]
    public async Task SyncAll_UpgradeBaselinePullsFirst_AndRetainsNewerHubContentOnBothDevices()
    {
        using var clientFile = new TempDatabase();
        using var hubFile = new TempDatabase();
        var id = Guid.NewGuid();
        var clientSetup = Guid.NewGuid();
        var hubSetup = Guid.NewGuid();
        foreach (var (path, setup, updated) in new[]
                 { (clientFile.DatabasePath, clientSetup, 20), (hubFile.DatabasePath, hubSetup, 30) })
        {
            using var legacy = new SQLiteConnection(path);
            legacy.Execute("CREATE TABLE board (id TEXT PRIMARY KEY, setup_id TEXT, updated INTEGER, client_updated INTEGER, deleted INTEGER)");
            legacy.Execute("INSERT INTO board VALUES (?, ?, ?, 10, NULL)", id, setup, updated);
        }
        var client = new TestPersistenceHarness(clientFile.DatabasePath);
        var hub = new TestPersistenceHarness(hubFile.DatabasePath);
        var requests = new List<string>();
        httpApiService.PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            requests.Add("pull");
            return await hub.GetSynchronizationDataAsync(call.Arg<long>(),
                await hub.GetSequenceUpperBoundAsync(SynchronizationDirection.Pull), SynchronizationDirection.Pull);
        });
        httpApiService.PushSyncAsync(Arg.Any<SynchronizationData>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            requests.Add("push");
            await hub.MergeAllAsync(call.Arg<SynchronizationData>());
        });

        var result = await harness.CreateCoordinator(client.SyncDataStore)
            .RunSyncAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<SynchronizationRunResult.Completed>(result);
        Assert.Equal(["pull", "push"], requests);
        Assert.Equal(hubSetup, (await client.GetAsync<Board>(id))!.SetupId);
        Assert.Equal(hubSetup, (await hub.GetAsync<Board>(id))!.SetupId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncAll_EditDuringRun_RemainsOutsideAcknowledgedBound_AndPushesOnNextRun(bool editDuringBaselinePull)
    {
        using var file = new TempDatabase();
        var database = new TestPersistenceHarness(file.DatabasePath);
        var board = new Board(Guid.NewGuid(), Guid.NewGuid());
        await database.PutAsync(board);
        var initialBound = await database.GetSequenceUpperBoundAsync(SynchronizationDirection.Push);
        var editedSetup = Guid.NewGuid();
        var pushes = new List<SynchronizationData>();
        var edited = false;
        async Task EditAsync()
        {
            if (edited) return;
            edited = true;
            board.SetupId = editedSetup;
            await database.PutAsync(board);
        }
        httpApiService.PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (editDuringBaselinePull) await EditAsync();
            return new SynchronizationData();
        });
        httpApiService.PushSyncAsync(Arg.Any<SynchronizationData>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            pushes.Add(call.Arg<SynchronizationData>());
            if (!editDuringBaselinePull) await EditAsync();
        });
        var service = harness.CreateCoordinator(database.SyncDataStore);

        Assert.IsType<SynchronizationRunResult.Completed>(await service.RunSyncAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(initialBound, await database.GetLastPushSequenceAsync(SyncTestServerHarness.Peer));
        Assert.IsType<SynchronizationRunResult.Completed>(await service.RunSyncAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, pushes.Count);
        var received = Assert.Single(pushes[1].Boards);
        Assert.Equal((editedSetup, 2L), (received.SetupId, received.Version));
        Assert.True(pushes[1].UpperBound > pushes[0].UpperBound);
    }

    [Fact]
    public async Task SyncAll_PushesSynchronizationDataFromDatabase()
    {
        var trackId = Guid.NewGuid();
        var localChanges = new SynchronizationData
        {
            Sessions =
            [
                new Session(Guid.NewGuid(), "session", "desc", null, 1234)
                {
                    Updated = 10
                }
            ],
            Tracks =
            [
                new Track
                {
                    Id = trackId,
                    Points =
                    [
                        new TrackPoint(1234, 1, 1, 0),
                        new TrackPoint(1235, 2, 2, 0)
                    ],
                    Updated = 9
                }
            ]
        };

        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(localChanges);
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });

        await CreateCoordinator().RunSyncAsync();

        await httpApiService.Received(1).PushSyncAsync(Arg.Is<SynchronizationData>(data =>
            ReferenceEquals(data, localChanges) && data.UpperBound == 9), Arg.Any<CancellationToken>());
        await syncDataStore.Received(1).UpdateLastPushSequenceAsync(
            SyncTestServerHarness.Peer,
            localChanges.UpperBound);
        await syncDataStore.Received(1).UpdateLastPullSequenceAsync(
            SyncTestServerHarness.Peer,
            12);
    }

    [Fact]
    public async Task SyncAll_UsesIndependentExclusivePushAndPullSequences()
    {
        var localChanges = new SynchronizationData();
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(8);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(40);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 8, Arg.Any<long>()).Returns(localChanges);
        httpApiService.PullSyncAsync(40, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 45 });

        await CreateCoordinator().RunSyncAsync();

        await syncDataStore.Received(1).GetSynchronizationDataAsync(SynchronizationDirection.Push,
            8,
            Arg.Is<long>(upper => upper == localChanges.UpperBound && upper > 0));
        await httpApiService.Received(1).PullSyncAsync(40, Arg.Any<CancellationToken>());
        await syncDataStore.Received(1).UpdateLastPushSequenceAsync(
            SyncTestServerHarness.Peer,
            localChanges.UpperBound);
        await syncDataStore.Received(1).UpdateLastPullSequenceAsync(
            SyncTestServerHarness.Peer,
            45);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, SyncFailureReason.Synchronize)]
    [InlineData(System.Net.HttpStatusCode.UpgradeRequired, SyncFailureReason.ProtocolMismatch)]
    public async Task SyncAll_DoesNotAdvanceEitherCursor_WhenPushFails(System.Net.HttpStatusCode status, SyncFailureReason expected)
    {
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PushSyncAsync(Arg.Any<SynchronizationData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("push failed", null, status)));

        Assert.Equal(expected, Assert.IsType<SynchronizationRunResult.Failed>(await CreateCoordinator().RunSyncAsync()).Reason);

        await syncDataStore.DidNotReceive().UpdateLastPushSequenceAsync(
            Arg.Any<PeerIdentity>(),
            Arg.Any<long>());
        await syncDataStore.DidNotReceive().UpdateLastPullSequenceAsync(
            Arg.Any<PeerIdentity>(),
            Arg.Any<long>());
        await httpApiService.DidNotReceive().PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAll_AppliesPulledChangesThroughRemoteSynchronizationPath()
    {
        var trackId = Guid.NewGuid();
        var remoteChanges = new SynchronizationData
        {
            Sessions =
            [
                new Session(Guid.NewGuid(), "remote", "desc", null, 1234)
                {
                    Updated = 20
                }
            ],
            Tracks =
            [
                new Track
                {
                    Id = trackId,
                    Points =
                    [
                        new TrackPoint(1234, 1, 1, 0),
                        new TrackPoint(1235, 2, 2, 0)
                    ],
                    Updated = 19
                }
            ],
        };

        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(remoteChanges);

        await CreateCoordinator().RunSyncAsync();

        await syncDataStore.Received(1).ApplyRemoteSynchronizationDataAsync(Arg.Is<SynchronizationData>(data =>
            data.Sessions.Count == 1 &&
            data.Tracks.Count == 1 &&
            data.Tracks[0].Id == trackId));
        await sessionRepository.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
    }



    [Fact]
    public async Task SyncAll_HoldsPullCursor_WhenSwapDoesNotResolve()
    {
        var sessionId = Guid.NewGuid();
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns(SyncTestServerHarness.ApplyResult(new SessionBlobSwap(sessionId, """{"target":true}""", new SessionProcessedGeneration(80))));
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(sessionId, "current", "", null));
        // The peer does not yet hold the target BLOB, so the swap cannot commit.
        httpApiService.GetSessionPsstAsync(sessionId, Arg.Any<CancellationToken>()).Returns(new SynchronizationTransferResult<SessionBlobPayload>(null, SynchronizationTransferStatus.Pending));

        var result = await CreateCoordinator().RunSyncAsync();

        // The pull cursor stays back so the next run re-derives and retries the swap;
        // advancing it would strand the swap permanently (BLOB writes do not bump updated).
        var incomplete = Assert.IsType<SynchronizationRunResult.IncompleteData>(result);
        Assert.Equal(new SynchronizationPendingTransfer(sessionId, SynchronizationTransferKind.ProcessedSession, SynchronizationDirection.Pull),
            Assert.Single(incomplete.PendingTransfers));
        await harness.SessionSyncApplier.DidNotReceive().ApplyDownloadedAsync(
            Arg.Any<SessionSnapshot>(), Arg.Any<SessionBlobPayload>(), Arg.Any<string?>(), Arg.Any<SessionProcessedGeneration?>());
        await syncDataStore.Received(1).UpdateLastPushSequenceAsync(
            SyncTestServerHarness.Peer,
            Arg.Any<long>());
        await syncDataStore.DidNotReceive().UpdateLastPullSequenceAsync(
            SyncTestServerHarness.Peer,
            Arg.Any<long>());
    }

    [Fact]
    public async Task SyncAll_PassesPreTransferGenerationToOwner_AndRetainsCursorWhenSuperseded()
    {
        var id = Guid.NewGuid();
        var current = new Session(id, "ride", "notes", null)
        {
            ProcessingFingerprintJson = "held", ProcessedTelemetryRevision = 1
        };
        sessionRepository.GetSessionAsync(id).Returns(_ => current);
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns(SyncTestServerHarness.ApplyResult(new SessionBlobSwap(id, "target", new SessionProcessedGeneration(80))));
        httpApiService.GetSessionPsstAsync(id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            current = new Session(id, "ride", "notes", null) { ProcessingFingerprintJson = "newer", ProcessedTelemetryRevision = 2 };
            return new SynchronizationTransferResult<SessionBlobPayload>(new SessionBlobPayload("target", [1, 2, 3]), SynchronizationTransferStatus.Completed);
        });
        harness.SessionSyncApplier.ApplyDownloadedAsync(Arg.Any<SessionSnapshot>(), Arg.Any<SessionBlobPayload>(), "target", new SessionProcessedGeneration(80))
            .Returns(SessionDataApplyResult.Pending);

        await CreateCoordinator().RunSyncAsync();

        await harness.SessionSyncApplier.Received(1).ApplyDownloadedAsync(
            Arg.Is<SessionSnapshot>(expected => expected.ProcessingFingerprintJson == "held" && expected.ProcessedTelemetryRevision == 1),
            Arg.Any<SessionBlobPayload>(), "target", new SessionProcessedGeneration(80));
        await syncDataStore.DidNotReceive().UpdateLastPullSequenceAsync(SyncTestServerHarness.Peer, Arg.Any<long>());
    }

    [Theory]
    [InlineData(SessionDataApplyResult.Applied)]
    [InlineData(SessionDataApplyResult.Ignored)]
    public async Task SyncAll_AdvancesPullCursor_WhenSwapIsAppliedOrIgnored(SessionDataApplyResult outcome)
    {
        var sessionId = Guid.NewGuid();
        const string target = """{"target":true}""";
        var targetGeneration = new SessionProcessedGeneration(
            DurationSeconds: 80);
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns(SyncTestServerHarness.ApplyResult(new SessionBlobSwap(
                sessionId,
                target,
                targetGeneration)));
        httpApiService.GetSessionPsstAsync(sessionId, Arg.Any<CancellationToken>()).Returns(new SynchronizationTransferResult<SessionBlobPayload>(new SessionBlobPayload(target, [1, 2, 3]), SynchronizationTransferStatus.Completed));
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(sessionId, "current", "", null));
        harness.SessionSyncApplier.ApplyDownloadedAsync(Arg.Any<SessionSnapshot>(), Arg.Any<SessionBlobPayload>(), target, targetGeneration)
            .Returns(outcome);

        await CreateCoordinator().RunSyncAsync();

        await harness.SessionSyncApplier.Received(1).ApplyDownloadedAsync(
            Arg.Is<SessionSnapshot>(snapshot => snapshot.Id == sessionId),
            Arg.Is<SessionBlobPayload>(payload => payload.Fingerprint == target), target, targetGeneration);
        await syncDataStore.Received(1).UpdateLastPushSequenceAsync(
            SyncTestServerHarness.Peer,
            Arg.Any<long>());
        await syncDataStore.Received(1).UpdateLastPullSequenceAsync(
            SyncTestServerHarness.Peer,
            12);
    }

    [Fact]
    public async Task SyncAll_CompletesFillDownloads_BeforeAnySwapDownloadStarts()
    {
        var fillId = Guid.NewGuid();
        var swapId = Guid.NewGuid();
        const string fillFingerprint = """{"fill":true}""";
        const string swapTarget = """{"target":true}""";
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns(SyncTestServerHarness.ApplyResult(new SessionBlobSwap(swapId, swapTarget, new SessionProcessedGeneration(80))));
        sessionRepository.GetIncompleteSessionIdsWithFingerprintAsync()
            .Returns([(fillId, fillFingerprint)]);

        var fillRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fillDownload = new TaskCompletionSource<SynchronizationTransferResult<SessionBlobPayload>>(TaskCreationOptions.RunContinuationsAsynchronously);
        httpApiService.GetSessionPsstAsync(fillId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fillRequested.TrySetResult();
            return fillDownload.Task;
        });
        httpApiService.GetSessionPsstAsync(swapId, Arg.Any<CancellationToken>()).Returns(new SynchronizationTransferResult<SessionBlobPayload>(new SessionBlobPayload(swapTarget, [1, 2, 3]), SynchronizationTransferStatus.Completed));
        sessionRepository.GetSessionAsync(fillId).Returns(new Session(fillId, "fill", "", null));
        sessionRepository.GetSessionAsync(swapId).Returns(new Session(swapId, "swap", "", null));

        var syncTask = CreateCoordinator().RunSyncAsync();

        // The swap pass must not start while a fill download is still pending.
        await fillRequested.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await httpApiService.DidNotReceive().GetSessionPsstAsync(swapId, Arg.Any<CancellationToken>());

        fillDownload.SetResult(new SynchronizationTransferResult<SessionBlobPayload>(new SessionBlobPayload(fillFingerprint, [4, 5, 6]), SynchronizationTransferStatus.Completed));
        await syncTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        await httpApiService.Received(1).GetSessionPsstAsync(swapId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAll_ReturnsCompleted_WhenCompletenessVerificationPasses()
    {
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });

        var result = await CreateCoordinator().RunSyncAsync();

        Assert.IsType<SynchronizationRunResult.Completed>(result);
    }

    [Fact]
    public async Task SyncAll_ReturnsIncompleteData_WhenCompletenessVerificationFindsMissingData()
    {
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        sessionRepository.GetIncompleteSessionIdsAsync().Returns([Guid.NewGuid(), Guid.NewGuid()]);
        recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync().Returns([Guid.NewGuid()]);

        var result = await CreateCoordinator().RunSyncAsync();

        var incomplete = Assert.IsType<SynchronizationRunResult.IncompleteData>(result);
        Assert.Equal(2, incomplete.MissingProcessedSessionCount);
        Assert.Equal(1, incomplete.IncompleteRecordedSourceCount);
        await syncDataStore.Received(1).UpdateLastPushSequenceAsync(
            SyncTestServerHarness.Peer,
            Arg.Any<long>());
        await syncDataStore.Received(1).UpdateLastPullSequenceAsync(
            SyncTestServerHarness.Peer,
            12);
    }

    [Fact]
    public async Task SyncAll_VerifiesCompletenessAfterPullingRecordedSources()
    {
        var calls = new List<string>();
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync()
            .Returns(_ =>
            {
                calls.Add("pull-sources");
                return Task.FromResult<IReadOnlyList<Guid>>([]);
            });
        sessionRepository.GetIncompleteSessionIdsAsync()
            .Returns(_ =>
            {
                calls.Add("verify-sessions");
                return Task.FromResult(new List<Guid>());
            });
        recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync()
            .Returns(_ =>
            {
                calls.Add("verify-sources");
                return Task.FromResult(new List<Guid>());
            });

        await CreateCoordinator().RunSyncAsync();

        Assert.Equal(["pull-sources", "verify-sessions", "verify-sources"], calls);
    }

    [Fact]
    public async Task SyncAll_ReportsDiscoveryAndSixTransferPhases()
    {
        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        var progress = new RecordingProgress<SynchronizationProgressSnapshot>();

        await CreateCoordinator().RunSyncAsync(progress);

        Assert.Equal(
            [
                SynchronizationPhase.ResolvingServer,
                SynchronizationPhase.PushingLocalChanges,
                SynchronizationPhase.PullingRemoteChanges,
                SynchronizationPhase.PushingIncompleteSessions,
                SynchronizationPhase.PullingIncompleteSessions,
                SynchronizationPhase.PushingIncompleteSessionSources,
                SynchronizationPhase.PullingIncompleteSessionSources,
            ],
            progress.Reports.Select(e => e.Phase).ToArray());
        Assert.All(progress.Reports, e => Assert.True(e.IsDeterminate));
        Assert.All(progress.Reports, e => Assert.Equal(7, e.TotalSteps));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], progress.Reports.Select(e => e.CurrentStep).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task SyncAll_PushesRecordedSourcesWithStoredHash(string? storedHash)
    {
        var source = SyncTestServerHarness.CreateRecordedSource();
        if (storedHash is not null)
        {
            source.SourceHash = storedHash;
        }

        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        httpApiService.GetIncompleteSessionSourceIdsAsync(Arg.Any<CancellationToken>()).Returns([source.SessionId]);
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(source.SessionId).Returns(source);

        await CreateCoordinator().RunSyncAsync();

        await httpApiService.Received(1).PatchRecordedSessionSourceAsync(Arg.Is<RecordedSessionSourcePayload>(transfer =>
            transfer.SessionId == source.SessionId &&
            transfer.SourceKind == source.SourceKind &&
            transfer.SourceName == source.SourceName &&
            transfer.SchemaVersion == source.SchemaVersion &&
            transfer.SourceHash == source.SourceHash &&
            transfer.Payload.SequenceEqual(source.Payload)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PulledRecordedSourceOutcome.Persisted)]
    [InlineData(PulledRecordedSourceOutcome.ExistingSource)]
    [InlineData(PulledRecordedSourceOutcome.InvalidHash)]
    [InlineData(PulledRecordedSourceOutcome.InvalidSstPayload)]
    [InlineData(PulledRecordedSourceOutcome.InvalidLivePayload)]
    [InlineData(PulledRecordedSourceOutcome.RepositoryFailure)]
    public async Task SyncAll_HandlesPulledRecordedSourceOutcome(PulledRecordedSourceOutcome outcome)
    {
        var source = SyncTestServerHarness.CreateRecordedSource();
        var transfer = outcome == PulledRecordedSourceOutcome.InvalidHash
            ? new RecordedSessionSourcePayload(
                source.SessionId,
                source.SourceKind,
                source.SourceName,
                source.SchemaVersion,
                "invalid",
                source.Payload)
            : SyncTestServerHarness.ToPayload(source);
        if (outcome is PulledRecordedSourceOutcome.InvalidSstPayload or PulledRecordedSourceOutcome.InvalidLivePayload)
            transfer = transfer with
            {
                SourceKind = outcome == PulledRecordedSourceOutcome.InvalidSstPayload
                    ? RecordedSessionSourceKind.ImportedSst : RecordedSessionSourceKind.LiveCapture,
                Payload = [1, 2, 3]
            };

        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([source.SessionId]);
        httpApiService.GetRecordedSessionSourceAsync(source.SessionId, Arg.Any<CancellationToken>()).Returns(new SynchronizationTransferResult<RecordedSessionSourcePayload>(transfer, SynchronizationTransferStatus.Completed));
        var storedSnapshot = RecordedSessionSourceSnapshot.From(source);
        if (outcome == PulledRecordedSourceOutcome.ExistingSource)
        {
            storedSnapshot = RecordedSessionSourceSnapshot.From(SyncTestServerHarness.CreateRecordedSource([1, 3, 5]))
                with { SessionId = source.SessionId };
            recordedSessionSourceRepository.PutRecordedSessionSourceAsync(Arg.Any<RecordedSessionSource>())
                .Returns(storedSnapshot);
        }
        if (outcome == PulledRecordedSourceOutcome.RepositoryFailure)
        {
            recordedSessionSourceRepository.PutRecordedSessionSourceAsync(Arg.Any<RecordedSessionSource>())
                .Returns(Task.FromException<RecordedSessionSourceSnapshot>(new InvalidOperationException("database busy")));

            Assert.Equal(SyncFailureReason.Synchronize, Assert.IsType<SynchronizationRunResult.Failed>(await CreateCoordinator().RunSyncAsync()).Reason);

            Assert.Null(sourceStore.Get(source.SessionId));
            await syncDataStore.Received(1).UpdateLastPushSequenceAsync(
                SyncTestServerHarness.Peer,
                Arg.Any<long>());
            await syncDataStore.Received(1).UpdateLastPullSequenceAsync(
                SyncTestServerHarness.Peer,
                12);
            return;
        }

        await CreateCoordinator().RunSyncAsync();

        if (outcome is PulledRecordedSourceOutcome.Persisted or PulledRecordedSourceOutcome.ExistingSource)
        {
            await recordedSessionSourceRepository.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
                saved.SessionId == source.SessionId &&
                saved.SourceKind == source.SourceKind &&
                saved.SourceName == source.SourceName &&
                saved.SchemaVersion == source.SchemaVersion &&
                saved.SourceHash == source.SourceHash &&
                saved.Payload.SequenceEqual(source.Payload)));
            Assert.Equal(storedSnapshot, sourceStore.Get(source.SessionId));
            return;
        }

        await recordedSessionSourceRepository.DidNotReceive().PutRecordedSessionSourceAsync(
            Arg.Any<RecordedSessionSource>());
        Assert.Null(sourceStore.Get(source.SessionId));
    }

    [Fact]
    public async Task SyncAll_PublishesPulledRecordedSourcesInOneBatch()
    {
        var first = SyncTestServerHarness.CreateRecordedSource();
        var second = SyncTestServerHarness.CreateRecordedSource();
        var firstTransfer = SyncTestServerHarness.ToPayload(first);
        var secondTransfer = SyncTestServerHarness.ToPayload(second);

        syncDataStore.GetLastPushSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetLastPullSequenceAsync(SyncTestServerHarness.Peer).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 5, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5, Arg.Any<CancellationToken>()).Returns(new SynchronizationData { UpperBound = 12 });
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([first.SessionId, second.SessionId]);
        httpApiService.GetRecordedSessionSourceAsync(first.SessionId, Arg.Any<CancellationToken>()).Returns(new SynchronizationTransferResult<RecordedSessionSourcePayload>(firstTransfer, SynchronizationTransferStatus.Completed));
        httpApiService.GetRecordedSessionSourceAsync(second.SessionId, Arg.Any<CancellationToken>()).Returns(new SynchronizationTransferResult<RecordedSessionSourcePayload>(secondTransfer, SynchronizationTransferStatus.Completed));

        await CreateCoordinator().RunSyncAsync();

        await recordedSessionSourceRepository.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
            saved.SessionId == first.SessionId &&
            saved.Payload.SequenceEqual(first.Payload)));
        await recordedSessionSourceRepository.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
            saved.SessionId == second.SessionId &&
            saved.Payload.SequenceEqual(second.Payload)));
        Assert.Equal(first.SourceHash, sourceStore.Get(first.SessionId)?.SourceHash);
        Assert.Equal(second.SourceHash, sourceStore.Get(second.SessionId)?.SourceHash);

    }

    [Fact]
    public async Task SyncAll_PublishesCommittedRows_WhenALaterPhaseFails()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var board = new Board(Guid.NewGuid(), null) { Version = 2, Updated = 20 };
        httpApiService.PullSyncAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new SynchronizationData { Boards = [board], UpperBound = 3 });
        httpApiService.GetIncompleteSessionIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<Guid>>(new IOException("peer disconnected")));
        var coordinator = harness.CreateCoordinator(fixture.Database.SyncDataStore, applier: fixture.CreateRowApplier());

        var result = await coordinator.RunSyncAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<SynchronizationRunResult.Failed>(result);
        await httpApiService.DidNotReceive().CompleteRunAsync(Arg.Any<CancellationToken>());
        Assert.Equal(board.Id, fixture.Boards.Get(board.Id)!.Id);
        Assert.Equal(board.Version, (await fixture.Database.GetAsync<Board>(board.Id))!.Version);
        Assert.Equal(0, await fixture.Database.GetLastPullSequenceAsync(SyncTestServerHarness.Peer));
    }

    [Fact]
    public async Task SyncAll_CancellationPublishesSourcesAlreadyCommittedInTheTransferBatch()
    {
        await using var fixture = new InboundSyncFixture(new InlineUiThreadDispatcher());
        var first = SyncTestServerHarness.CreateRecordedSource();
        var second = Guid.NewGuid();
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([first.SessionId, second]);
        httpApiService.GetRecordedSessionSourceAsync(first.SessionId, Arg.Any<CancellationToken>())
            .Returns(new SynchronizationTransferResult<RecordedSessionSourcePayload>(SyncTestServerHarness.ToPayload(first), SynchronizationTransferStatus.Completed));
        httpApiService.GetRecordedSessionSourceAsync(second, Arg.Any<CancellationToken>()).Returns(call =>
            new TaskCompletionSource<SynchronizationTransferResult<RecordedSessionSourcePayload>>().Task.WaitAsync(call.Arg<CancellationToken>()));
        recordedSessionSourceRepository.PutRecordedSessionSourceAsync(Arg.Any<RecordedSessionSource>()).Returns(async call =>
        {
            var snapshot = await fixture.Database.SourceRepository.PutRecordedSessionSourceAsync(call.Arg<RecordedSessionSource>());
            committed.SetResult();
            return snapshot;
        });
        using var cancellation = new CancellationTokenSource();
        var coordinator = CreateCoordinator();
        var run = coordinator.RunSyncAsync(cancellationToken: cancellation.Token);
        await committed.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();

        Assert.Null(await run.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
        await httpApiService.DidNotReceive().CompleteRunAsync(Arg.Any<CancellationToken>());
        Assert.Equal(first.SourceHash, sourceStore.Get(first.SessionId)?.SourceHash);
        Assert.Null(sourceStore.Get(second));
        Assert.Equal(first.Payload, (await fixture.Database.GetRecordedSessionSourceAsync(first.SessionId))!.Payload);
        Assert.False(coordinator.IsRunning);
    }

    public void Dispose() => harness.Dispose();

    public enum PulledRecordedSourceOutcome
    {
        Persisted,
        ExistingSource,
        InvalidHash,
        InvalidSstPayload,
        InvalidLivePayload,
        RepositoryFailure
    }

}
