using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Sync;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.Extensibility.Sync;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sync;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizationClientServiceTests
{
    private readonly SyncTestServerHarness harness = new();

    private ISyncDataStore syncDataStore => harness.SyncDataStore;
    private ISessionRepository sessionRepository => harness.SessionRepository;
    private ISessionStoreWriter sessionStore => harness.SessionStore;
    private IRecordedSessionSourceRepository recordedSessionSourceRepository => harness.RecordedSessionSourceRepository;
    private IRecordedSessionSourceStoreWriter sourceStore => harness.SourceStore;
    private IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery => harness.RecordedSessionSourceSyncQuery;
    private IHttpApiService httpApiService => harness.HttpApiService;
    private IAppPreferences appPreferences => harness.AppPreferences;

    private SynchronizationClientService CreateService(IExtensionSyncService? extensionSync = null) =>
        harness.CreateSynchronizationClientService(extensionSync);

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
                    FullTrack = trackId,
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

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(localChanges);
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });

        await CreateService().SyncAll();

        await httpApiService.Received(1).PushSyncAsync(Arg.Is<SynchronizationData>(data =>
            ReferenceEquals(data, localChanges) && data.UpperBound > 0));
        await syncDataStore.Received(1).UpdateLastPushTimeAsync(
            SynchronizationClientService.SyncStateKey,
            localChanges.UpperBound);
        await syncDataStore.Received(1).UpdateLastPullTimeAsync(
            SynchronizationClientService.SyncStateKey,
            12);
    }

    [Fact]
    public async Task SyncAll_UsesIndependentOverlappedPushAndPullWindows()
    {
        var localChanges = new SynchronizationData();
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(8);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(40);
        syncDataStore.GetSynchronizationDataAsync(7, Arg.Any<long>()).Returns(localChanges);
        httpApiService.PullSyncAsync(39).Returns(new SynchronizationData { UpperBound = 45 });

        await CreateService().SyncAll();

        await syncDataStore.Received(1).GetSynchronizationDataAsync(
            7,
            Arg.Is<long>(upper => upper == localChanges.UpperBound && upper > 0));
        await appPreferences.Received(1).GetSyncDataAsync(7, localChanges.UpperBound);
        await httpApiService.Received(1).PullSyncAsync(39);
        await syncDataStore.Received(1).UpdateLastPushTimeAsync(
            SynchronizationClientService.SyncStateKey,
            localChanges.UpperBound);
        await syncDataStore.Received(1).UpdateLastPullTimeAsync(
            SynchronizationClientService.SyncStateKey,
            45);
    }

    [Fact]
    public async Task SyncAll_DoesNotAdvanceEitherCursor_WhenPushFails()
    {
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PushSyncAsync(Arg.Any<SynchronizationData>())
            .Returns(Task.FromException(new InvalidOperationException("push failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().SyncAll());

        await syncDataStore.DidNotReceive().UpdateLastPushTimeAsync(
            Arg.Any<string?>(),
            Arg.Any<long>());
        await syncDataStore.DidNotReceive().UpdateLastPullTimeAsync(
            Arg.Any<string?>(),
            Arg.Any<long>());
        await httpApiService.DidNotReceive().PullSyncAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task SyncAll_AppliesPulledChangesThroughRemoteSynchronizationPath()
    {
        var trackId = Guid.NewGuid();
        var remotePreferences = new AppPreferencesSyncData
        {
            Updated = 21,
            Maps = new MapPreferencesSyncData
            {
                SelectedLayerId = Guid.NewGuid(),
            },
        };
        var remoteChanges = new SynchronizationData
        {
            Sessions =
            [
                new Session(Guid.NewGuid(), "remote", "desc", null, 1234)
                {
                    FullTrack = trackId,
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
            AppPreferences = remotePreferences,
        };

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(remoteChanges);

        await CreateService().SyncAll();

        await syncDataStore.Received(1).ApplyRemoteSynchronizationDataAsync(Arg.Is<SynchronizationData>(data =>
            data.Sessions.Count == 1 &&
            data.Tracks.Count == 1 &&
            data.Tracks[0].Id == trackId));
        await appPreferences.Received(1).ApplySyncDataAsync(remotePreferences);
        await sessionRepository.DidNotReceive().PutSessionAsync(Arg.Any<Session>());
    }

    [Fact]
    public async Task SyncAll_PushesAppPreferencesChangedSinceLastSync()
    {
        var localPreferences = new AppPreferencesSyncData
        {
            Updated = 8,
            Maps = new MapPreferencesSyncData
            {
                SelectedLayerId = Guid.NewGuid(),
            },
        };

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        appPreferences.GetSyncDataAsync(4, Arg.Any<long>()).Returns(localPreferences);
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });

        await CreateService().SyncAll();

        await httpApiService.Received(1).PushSyncAsync(Arg.Is<SynchronizationData>(data =>
            ReferenceEquals(data.AppPreferences, localPreferences)));
    }

    [Fact]
    public async Task SyncAll_AddsExtensionBatchesToPushPayload()
    {
        var envelope = CreateExtensionEnvelope("test");
        var extensionSync = new FakeExtensionSyncService
        {
            CreateBatchesResult = [envelope]
        };

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });

        await CreateService(extensionSync).SyncAll();

        await httpApiService.Received(1).PushSyncAsync(Arg.Is<SynchronizationData>(data =>
            data.ExtensionBatches.Count == 1 &&
            data.ExtensionBatches[0] == envelope));
    }

    [Fact]
    public async Task SyncAll_AppliesPulledExtensionBatchesAfterCoreDataAndPreferences()
    {
        var envelope = CreateExtensionEnvelope("test");
        var calls = new List<string>();
        var extensionSync = new FakeExtensionSyncService
        {
            PrepareBatchesAsyncOverride = (_, _) =>
            {
                calls.Add("prepare");
                return Task.FromResult(new ExtensionSyncApplyPlan([]));
            },
            ApplyPreparedBatchesAsyncOverride = (_, _, _, _, _) =>
            {
                calls.Add("extension");
                return Task.FromResult<IReadOnlyList<SynchronizationProgressSnapshot>>([]);
            }
        };
        var remoteChanges = new SynchronizationData
        {
            ExtensionBatches = [envelope],
            AppPreferences = new AppPreferencesSyncData
            {
                Updated = 10,
            },
        };

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        syncDataStore.ApplyRemoteSynchronizationDataAsync(remoteChanges)
            .Returns(_ =>
            {
                calls.Add("core");
                return (IReadOnlyList<SessionBlobSwap>)Array.Empty<SessionBlobSwap>();
            });
        appPreferences.ApplySyncDataAsync(remoteChanges.AppPreferences)
            .Returns(_ =>
            {
                calls.Add("preferences");
                return Task.CompletedTask;
            });
        syncDataStore.UpdateLastPullTimeAsync(
                SynchronizationClientService.SyncStateKey,
                Arg.Any<long>())
            .Returns(_ =>
            {
                calls.Add("pull-cursor");
                return Task.CompletedTask;
            });
        httpApiService.PullSyncAsync(4).Returns(remoteChanges);

        await CreateService(extensionSync).SyncAll();

        Assert.Equal(["prepare", "core", "preferences", "extension", "pull-cursor"], calls);
    }

    [Fact]
    public async Task SyncAll_DoesNotMutatePulledState_WhenExtensionPreparationFails()
    {
        var extensionSync = new FakeExtensionSyncService
        {
            PrepareBatchesAsyncOverride = (_, _) =>
                Task.FromException<ExtensionSyncApplyPlan>(
                    new InvalidOperationException("extension payload invalid")),
        };
        var remoteChanges = new SynchronizationData
        {
            ExtensionBatches = [CreateExtensionEnvelope("test")],
        };
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(remoteChanges);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(extensionSync).SyncAll());

        await syncDataStore.DidNotReceive().ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>());
        await appPreferences.DidNotReceive().ApplySyncDataAsync(Arg.Any<AppPreferencesSyncData?>());
        await syncDataStore.DidNotReceive().UpdateLastPullTimeAsync(
            Arg.Any<string?>(),
            Arg.Any<long>());
    }

    [Fact]
    public async Task SyncAll_ReturnsPartialApplyAndHoldsPullCursor_WhenExtensionApplyFails()
    {
        var extensionSync = new FakeExtensionSyncService
        {
            ApplyPreparedBatchesAsyncOverride = (_, _, _, _, _) =>
                Task.FromException<IReadOnlyList<SynchronizationProgressSnapshot>>(
                    new InvalidOperationException("extension failed"))
        };
        var remoteChanges = new SynchronizationData
        {
            ExtensionBatches = [CreateExtensionEnvelope("test")],
        };

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(remoteChanges);

        var result = await CreateService(extensionSync).SyncAll();

        var partialApply = Assert.IsType<SynchronizationRunResult.PartialApply>(result);
        Assert.Equal("extension failed", partialApply.ErrorMessage);
        await syncDataStore.Received(1).ApplyRemoteSynchronizationDataAsync(remoteChanges);
        await appPreferences.Received(1).ApplySyncDataAsync(remoteChanges.AppPreferences);
        await syncDataStore.Received(1).UpdateLastPushTimeAsync(
            SynchronizationClientService.SyncStateKey,
            Arg.Any<long>());
        await syncDataStore.DidNotReceive().UpdateLastPullTimeAsync(
            SynchronizationClientService.SyncStateKey,
            Arg.Any<long>());
    }

    [Fact]
    public async Task SyncAll_HoldsPullCursor_WhenSwapDoesNotResolve()
    {
        var sessionId = Guid.NewGuid();
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns((IReadOnlyList<SessionBlobSwap>)[new SessionBlobSwap(sessionId, """{"target":true}""")]);
        // The peer does not yet hold the target BLOB, so the swap cannot commit.
        httpApiService.GetSessionPsstAsync(sessionId).Returns((SessionBlobPayload?)null);

        var result = await CreateService().SyncAll();

        // The pull cursor stays back so the next run re-derives and retries the swap;
        // advancing it would strand the swap permanently (BLOB writes do not bump updated).
        Assert.IsType<SynchronizationRunResult.Completed>(result);
        await sessionStore.DidNotReceive().CommitPsstSwapAsync(
            Arg.Any<Guid>(),
            Arg.Any<byte[]>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await syncDataStore.Received(1).UpdateLastPushTimeAsync(
            SynchronizationClientService.SyncStateKey,
            Arg.Any<long>());
        await syncDataStore.DidNotReceive().UpdateLastPullTimeAsync(
            SynchronizationClientService.SyncStateKey,
            Arg.Any<long>());
    }

    [Fact]
    public async Task SyncAll_CommitsSwapAndAdvancesPullCursor_WhenDownloadedFingerprintMatchesTarget()
    {
        var sessionId = Guid.NewGuid();
        const string target = """{"target":true}""";
        var targetGeneration = new SessionProcessedGeneration(
            DurationSeconds: 80,
            DistanceMeters: 20,
            AscentMeters: 8,
            DescentMeters: 3,
            FullTrackId: Guid.NewGuid(),
            GpsOffsetSeconds: 1.25,
            Track: [new TrackPoint(100, 1, 2, 3)]);
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns((IReadOnlyList<SessionBlobSwap>)[new SessionBlobSwap(
                sessionId,
                target,
                targetGeneration)]);
        httpApiService.GetSessionPsstAsync(sessionId).Returns(new SessionBlobPayload(target, [1, 2, 3]));
        sessionStore
            .CommitPsstSwapAsync(
                sessionId,
                Arg.Any<byte[]>(),
                target,
                targetGeneration,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreMutationResult<SessionSnapshot>>(
                new StoreMutationResult<SessionSnapshot>.Saved(TestSnapshots.Session(id: sessionId))));

        await CreateService().SyncAll();

        await sessionStore.Received(1).CommitPsstSwapAsync(
            sessionId,
            Arg.Any<byte[]>(),
            target,
            targetGeneration,
            Arg.Any<CancellationToken>());
        await syncDataStore.Received(1).UpdateLastPushTimeAsync(
            SynchronizationClientService.SyncStateKey,
            Arg.Any<long>());
        await syncDataStore.Received(1).UpdateLastPullTimeAsync(
            SynchronizationClientService.SyncStateKey,
            12);
    }

    [Fact]
    public async Task SyncAll_CompletesFillDownloads_BeforeAnySwapDownloadStarts()
    {
        var fillId = Guid.NewGuid();
        var swapId = Guid.NewGuid();
        const string fillFingerprint = """{"fill":true}""";
        const string swapTarget = """{"target":true}""";
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns((IReadOnlyList<SessionBlobSwap>)[new SessionBlobSwap(swapId, swapTarget)]);
        sessionRepository.GetIncompleteSessionIdsWithFingerprintAsync()
            .Returns([(fillId, fillFingerprint)]);

        var fillRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fillDownload = new TaskCompletionSource<SessionBlobPayload?>(TaskCreationOptions.RunContinuationsAsynchronously);
        httpApiService.GetSessionPsstAsync(fillId).Returns(_ =>
        {
            fillRequested.TrySetResult();
            return fillDownload.Task;
        });
        httpApiService.GetSessionPsstAsync(swapId).Returns(new SessionBlobPayload(swapTarget, [1, 2, 3]));
        sessionStore
            .CommitPsstSwapAsync(Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<StoreMutationResult<SessionSnapshot>>(
                new StoreMutationResult<SessionSnapshot>.Saved(TestSnapshots.Session(id: callInfo.Arg<Guid>()))));

        var syncTask = CreateService().SyncAll();

        // The swap pass must not start while a fill download is still pending.
        await fillRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await httpApiService.DidNotReceive().GetSessionPsstAsync(swapId);

        fillDownload.SetResult(new SessionBlobPayload(fillFingerprint, [4, 5, 6]));
        await syncTask.WaitAsync(TimeSpan.FromSeconds(2));

        await httpApiService.Received(1).GetSessionPsstAsync(swapId);
    }

    [Fact]
    public async Task SyncAll_ReturnsCompleted_WhenCompletenessVerificationPasses()
    {
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });

        var result = await CreateService().SyncAll();

        Assert.IsType<SynchronizationRunResult.Completed>(result);
    }

    [Fact]
    public async Task SyncAll_ReturnsIncompleteLocalData_WhenCompletenessVerificationFindsMissingData()
    {
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        sessionRepository.GetIncompleteSessionIdsAsync().Returns([Guid.NewGuid(), Guid.NewGuid()]);
        recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync().Returns([Guid.NewGuid()]);

        var result = await CreateService().SyncAll();

        var incomplete = Assert.IsType<SynchronizationRunResult.IncompleteLocalData>(result);
        Assert.Equal(2, incomplete.MissingProcessedSessionCount);
        Assert.Equal(1, incomplete.IncompleteRecordedSourceCount);
        await syncDataStore.Received(1).UpdateLastPushTimeAsync(
            SynchronizationClientService.SyncStateKey,
            Arg.Any<long>());
        await syncDataStore.Received(1).UpdateLastPullTimeAsync(
            SynchronizationClientService.SyncStateKey,
            12);
    }

    [Fact]
    public async Task SyncAll_VerifiesCompletenessAfterPullingRecordedSources()
    {
        var calls = new List<string>();
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
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

        await CreateService().SyncAll();

        Assert.Equal(["pull-sources", "verify-sessions", "verify-sources"], calls);
    }

    [Fact]
    public async Task SyncAll_ReportsExtensionProgressMessages()
    {
        var remoteChanges = new SynchronizationData
        {
            ExtensionBatches = [CreateExtensionEnvelope("test")],
        };
        var extensionProgress = new SynchronizationProgressSnapshot(
            SynchronizationPhase.PullingRemoteChanges,
            "Applied extension batch",
            CurrentStep: 2,
            TotalSteps: 6,
            IsDeterminate: true);
        var extensionSync = new FakeExtensionSyncService
        {
            ApplyBatchesResult = [extensionProgress]
        };
        var events = new List<SynchronizationProgressSnapshot>();

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(remoteChanges);

        await CreateService(extensionSync).SyncAll(new ProgressCapture(events));

        Assert.Contains(events, progress => progress.Message == "Applied extension batch");
    }

    [Fact]
    public async Task SyncAll_ReportsSixServicePhaseProgress_WhenProgressIsProvided()
    {
        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        var events = new List<SynchronizationProgressSnapshot>();

        await CreateService().SyncAll(new ProgressCapture(events));

        Assert.Equal(
            [
                SynchronizationPhase.PushingLocalChanges,
                SynchronizationPhase.PullingRemoteChanges,
                SynchronizationPhase.PushingIncompleteSessions,
                SynchronizationPhase.PullingIncompleteSessions,
                SynchronizationPhase.PushingIncompleteSessionSources,
                SynchronizationPhase.PullingIncompleteSessionSources,
            ],
            events.Select(e => e.Phase).ToArray());
        Assert.All(events, e => Assert.True(e.IsDeterminate));
        Assert.All(events, e => Assert.Equal(6, e.TotalSteps));
        Assert.Equal([1, 2, 3, 4, 5, 6], events.Select(e => e.CurrentStep).ToArray());
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

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        httpApiService.GetIncompleteSessionSourceIdsAsync().Returns([source.SessionId]);
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(source.SessionId).Returns(source);

        await CreateService().SyncAll();

        await httpApiService.Received(1).PatchRecordedSessionSourceAsync(Arg.Is<RecordedSessionSourcePayload>(transfer =>
            transfer.SessionId == source.SessionId &&
            transfer.SourceKind == source.SourceKind &&
            transfer.SourceName == source.SourceName &&
            transfer.SchemaVersion == source.SchemaVersion &&
            transfer.SourceHash == source.SourceHash &&
            transfer.Payload.SequenceEqual(source.Payload)));
    }

    [Theory]
    [InlineData(PulledRecordedSourceOutcome.Persisted)]
    [InlineData(PulledRecordedSourceOutcome.InvalidHash)]
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

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([source.SessionId]);
        httpApiService.GetRecordedSessionSourceAsync(source.SessionId).Returns(transfer);
        if (outcome == PulledRecordedSourceOutcome.RepositoryFailure)
        {
            recordedSessionSourceRepository.PutRecordedSessionSourceAsync(Arg.Any<RecordedSessionSource>())
                .Returns(Task.FromException(new InvalidOperationException("database busy")));

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().SyncAll());

            await sourceStore.DidNotReceive().PublishSourcesChangedAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>());
            await syncDataStore.Received(1).UpdateLastPushTimeAsync(
                SynchronizationClientService.SyncStateKey,
                Arg.Any<long>());
            await syncDataStore.Received(1).UpdateLastPullTimeAsync(
                SynchronizationClientService.SyncStateKey,
                12);
            return;
        }

        await CreateService().SyncAll();

        if (outcome == PulledRecordedSourceOutcome.Persisted)
        {
            await recordedSessionSourceRepository.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
                saved.SessionId == source.SessionId &&
                saved.SourceKind == source.SourceKind &&
                saved.SourceName == source.SourceName &&
                saved.SchemaVersion == source.SchemaVersion &&
                saved.SourceHash == source.SourceHash &&
                saved.Payload.SequenceEqual(source.Payload)));
            await sourceStore.Received(1).PublishSourcesChangedAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(source.SessionId)),
                Arg.Any<CancellationToken>());
            return;
        }

        await recordedSessionSourceRepository.DidNotReceive().PutRecordedSessionSourceAsync(
            Arg.Any<RecordedSessionSource>());
        await sourceStore.DidNotReceive().PublishSourcesChangedAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAll_PublishesPulledRecordedSourcesInOneBatch()
    {
        var first = SyncTestServerHarness.CreateRecordedSource();
        var second = SyncTestServerHarness.CreateRecordedSource();
        var firstTransfer = SyncTestServerHarness.ToPayload(first);
        var secondTransfer = SyncTestServerHarness.ToPayload(second);

        syncDataStore.GetLastPushTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetLastPullTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(4, Arg.Any<long>()).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(4).Returns(new SynchronizationData { UpperBound = 12 });
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([first.SessionId, second.SessionId]);
        httpApiService.GetRecordedSessionSourceAsync(first.SessionId).Returns(firstTransfer);
        httpApiService.GetRecordedSessionSourceAsync(second.SessionId).Returns(secondTransfer);

        await CreateService().SyncAll();

        await recordedSessionSourceRepository.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
            saved.SessionId == first.SessionId &&
            saved.Payload.SequenceEqual(first.Payload)));
        await recordedSessionSourceRepository.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
            saved.SessionId == second.SessionId &&
            saved.Payload.SequenceEqual(second.Payload)));
        await sourceStore.Received(1).PublishSourcesChangedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 2 &&
                ids.Contains(first.SessionId) &&
                ids.Contains(second.SessionId)),
            Arg.Any<CancellationToken>());
    }

    private static ExtensionSyncEnvelope CreateExtensionEnvelope(string extensionId) => new(
        extensionId,
        SchemaVersion: 1,
        ContentType: "application/test",
        Payload: [1, 2, 3]);

    public enum PulledRecordedSourceOutcome
    {
        Persisted,
        InvalidHash,
        RepositoryFailure
    }

    private sealed class ProgressCapture(List<SynchronizationProgressSnapshot> events) : IProgress<SynchronizationProgressSnapshot>
    {
        public void Report(SynchronizationProgressSnapshot value)
        {
            events.Add(value);
        }
    }

    private sealed class FakeExtensionSyncService : IExtensionSyncService
    {
        public List<ExtensionSyncEnvelope> CreateBatchesResult { get; init; } = [];
        public IReadOnlyList<SynchronizationProgressSnapshot> ApplyBatchesResult { get; init; } = [];

        public Func<
            IEnumerable<ExtensionSyncEnvelope>,
            CancellationToken,
            Task<ExtensionSyncApplyPlan>>? PrepareBatchesAsyncOverride { get; init; }

        public Func<
            ExtensionSyncApplyPlan,
            SynchronizationPhase,
            int,
            int,
            CancellationToken,
            Task<IReadOnlyList<SynchronizationProgressSnapshot>>>? ApplyPreparedBatchesAsyncOverride { get; init; }

        public Task<List<ExtensionSyncEnvelope>> CreateBatchesAsync(
            long sinceExclusive,
            long upperInclusive,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateBatchesResult);

        public Task<ExtensionSyncApplyPlan> PrepareBatchesAsync(
            IEnumerable<ExtensionSyncEnvelope> envelopes,
            CancellationToken cancellationToken = default) =>
            PrepareBatchesAsyncOverride?.Invoke(envelopes, cancellationToken)
            ?? Task.FromResult(new ExtensionSyncApplyPlan([]));

        public Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyPreparedBatchesAsync(
            ExtensionSyncApplyPlan plan,
            SynchronizationPhase phase,
            int currentStep,
            int totalSteps,
            CancellationToken cancellationToken = default) =>
            ApplyPreparedBatchesAsyncOverride?.Invoke(
                plan,
                phase,
                currentStep,
                totalSteps,
                cancellationToken)
            ?? Task.FromResult(ApplyBatchesResult);

        public async Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyBatchesAsync(
            IEnumerable<ExtensionSyncEnvelope> envelopes,
            SynchronizationPhase phase,
            int currentStep,
            int totalSteps,
            CancellationToken cancellationToken = default)
        {
            var plan = await PrepareBatchesAsync(envelopes, cancellationToken);
            return await ApplyPreparedBatchesAsync(
                plan,
                phase,
                currentStep,
                totalSteps,
                cancellationToken);
        }
    }
}
