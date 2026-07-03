using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Sync;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.Extensibility.Sync;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.Tests.SyncAndPairing.Services;

public class SynchronizationClientServiceTests
{
    private readonly ISyncDataStore syncDataStore = Substitute.For<ISyncDataStore>();
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTelemetryWriter sessionTelemetryWriter = Substitute.For<ISessionTelemetryWriter>();
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
    private readonly IRecordedSessionSourceSyncQuery recordedSessionSourceSyncQuery = Substitute.For<IRecordedSessionSourceSyncQuery>();
    private readonly IHttpApiService httpApiService = Substitute.For<IHttpApiService>();
    private readonly IAppPreferences appPreferences = Substitute.For<IAppPreferences>();

    public SynchronizationClientServiceTests()
    {
        httpApiService.ServerUrl.Returns("https://temporary-sync-endpoint.test");
        httpApiService.GetIncompleteSessionIdsAsync().Returns([]);
        sessionRepository.GetIncompleteSessionIdsWithFingerprintAsync().Returns([]);
        httpApiService.GetIncompleteSessionSourceIdsAsync().Returns([]);
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([]);
        appPreferences.GetSyncDataAsync(Arg.Any<long>()).Returns((AppPreferencesSyncData?)null);
        appPreferences.ApplySyncDataAsync(Arg.Any<AppPreferencesSyncData?>()).Returns(Task.CompletedTask);
    }

    private SynchronizationClientService CreateService(IExtensionSyncService? extensionSync = null)
    {
        return new SynchronizationClientService(
            syncDataStore,
            sessionRepository,
            sessionTelemetryWriter,
            recordedSessionSourceRepository,
            recordedSessionSourceSyncQuery,
            httpApiService,
            appPreferences,
            extensionSync);
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

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(localChanges);
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());

        await CreateService().SyncAll();

        await httpApiService.Received(1).PushSyncAsync(Arg.Is<SynchronizationData>(data => ReferenceEquals(data, localChanges)));
        await syncDataStore.Received(1).UpdateLastSyncTimeAsync(SynchronizationClientService.SyncStateKey);
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

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(remoteChanges);

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

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        appPreferences.GetSyncDataAsync(5).Returns(localPreferences);
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());

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

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());

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
            ApplyBatchesAsyncOverride = (_, _, _, _, _) =>
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

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
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
        syncDataStore.UpdateLastSyncTimeAsync(SynchronizationClientService.SyncStateKey)
            .Returns(_ =>
            {
                calls.Add("last-sync");
                return Task.CompletedTask;
            });
        httpApiService.PullSyncAsync(5).Returns(remoteChanges);

        await CreateService(extensionSync).SyncAll();

        Assert.Equal(["core", "preferences", "extension", "last-sync"], calls);
    }

    [Fact]
    public async Task SyncAll_DoesNotAdvanceLastSync_WhenExtensionApplyFails()
    {
        var extensionSync = new FakeExtensionSyncService
        {
            ApplyBatchesAsyncOverride = (_, _, _, _, _) =>
                Task.FromException<IReadOnlyList<SynchronizationProgressSnapshot>>(
                    new InvalidOperationException("extension failed"))
        };
        var remoteChanges = new SynchronizationData
        {
            ExtensionBatches = [CreateExtensionEnvelope("test")],
        };

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(remoteChanges);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(extensionSync).SyncAll());

        await syncDataStore.DidNotReceive().UpdateLastSyncTimeAsync(SynchronizationClientService.SyncStateKey);
    }

    [Fact]
    public async Task SyncAll_HoldsWatermark_WhenSwapDoesNotResolve()
    {
        var sessionId = Guid.NewGuid();
        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns((IReadOnlyList<SessionBlobSwap>)[new SessionBlobSwap(sessionId, """{"target":true}""")]);
        // The peer does not yet hold the target BLOB, so the swap cannot commit.
        httpApiService.GetSessionPsstAsync(sessionId).Returns((SessionDataTransfer?)null);

        await CreateService().SyncAll();

        // The watermark stays back so the next run re-derives and retries the swap;
        // advancing it would strand the swap permanently (BLOB writes do not bump updated).
        await sessionTelemetryWriter.DidNotReceive().SwapSessionPsstAsync(
            Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string?>());
        await syncDataStore.DidNotReceive().UpdateLastSyncTimeAsync(SynchronizationClientService.SyncStateKey);
    }

    [Fact]
    public async Task SyncAll_CommitsSwapAndAdvancesWatermark_WhenDownloadedFingerprintMatchesTarget()
    {
        var sessionId = Guid.NewGuid();
        const string target = """{"target":true}""";
        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        syncDataStore.ApplyRemoteSynchronizationDataAsync(Arg.Any<SynchronizationData>())
            .Returns((IReadOnlyList<SessionBlobSwap>)[new SessionBlobSwap(sessionId, target)]);
        httpApiService.GetSessionPsstAsync(sessionId).Returns(new SessionDataTransfer(target, [1, 2, 3]));

        await CreateService().SyncAll();

        await sessionTelemetryWriter.Received(1).SwapSessionPsstAsync(sessionId, Arg.Any<byte[]>(), target);
        await syncDataStore.Received(1).UpdateLastSyncTimeAsync(SynchronizationClientService.SyncStateKey);
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

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(remoteChanges);

        await CreateService(extensionSync).SyncAll(new ProgressCapture(events));

        Assert.Contains(events, progress => progress.Message == "Applied extension batch");
    }

    [Fact]
    public async Task SyncAll_ReportsSixServicePhaseProgress_WhenProgressIsProvided()
    {
        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
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

    [Fact]
    public async Task SyncAll_PushesMissingRecordedSourcesToServer()
    {
        var source = CreateRecordedSource();

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        httpApiService.GetIncompleteSessionSourceIdsAsync().Returns([source.SessionId]);
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(source.SessionId).Returns(source);

        await CreateService().SyncAll();

        await httpApiService.Received(1).PatchRecordedSessionSourceAsync(Arg.Is<RecordedSessionSourceTransfer>(transfer =>
            transfer.SessionId == source.SessionId &&
            transfer.SourceKind == source.SourceKind &&
            transfer.SourceName == source.SourceName &&
            transfer.SchemaVersion == source.SchemaVersion &&
            transfer.SourceHash == source.SourceHash &&
            transfer.Payload.SequenceEqual(source.Payload)));
    }

    [Fact]
    public async Task SyncAll_DoesNotPushRecordedSource_WhenHashDoesNotMatchPayload()
    {
        var source = CreateRecordedSource();
        source.SourceHash = "invalid";

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        httpApiService.GetIncompleteSessionSourceIdsAsync().Returns([source.SessionId]);
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(source.SessionId).Returns(source);

        await CreateService().SyncAll();

        await httpApiService.DidNotReceive().PatchRecordedSessionSourceAsync(Arg.Any<RecordedSessionSourceTransfer>());
    }

    [Fact]
    public async Task SyncAll_PullsMissingRecordedSourcesFromServer()
    {
        var source = CreateRecordedSource();
        var transfer = new RecordedSessionSourceTransfer(
            source.SessionId,
            source.SourceKind,
            source.SourceName,
            source.SchemaVersion,
            source.SourceHash,
            source.Payload);

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([source.SessionId]);
        httpApiService.GetRecordedSessionSourceAsync(source.SessionId).Returns(transfer);

        await CreateService().SyncAll();

        await recordedSessionSourceRepository.Received(1).PutRecordedSessionSourceAsync(Arg.Is<RecordedSessionSource>(saved =>
            saved.SessionId == source.SessionId &&
            saved.SourceKind == source.SourceKind &&
            saved.SourceName == source.SourceName &&
            saved.SchemaVersion == source.SchemaVersion &&
            saved.SourceHash == source.SourceHash &&
            saved.Payload.SequenceEqual(source.Payload)));
    }

    [Fact]
    public async Task SyncAll_DoesNotPersistPulledRecordedSource_WhenHashDoesNotMatchPayload()
    {
        var source = CreateRecordedSource();
        var transfer = new RecordedSessionSourceTransfer(
            source.SessionId,
            source.SourceKind,
            source.SourceName,
            source.SchemaVersion,
            "invalid",
            source.Payload);

        syncDataStore.GetLastSyncTimeAsync(SynchronizationClientService.SyncStateKey).Returns(5);
        syncDataStore.GetSynchronizationDataAsync(5).Returns(new SynchronizationData());
        httpApiService.PullSyncAsync(5).Returns(new SynchronizationData());
        recordedSessionSourceSyncQuery.GetSourceSyncTargetIdsAsync().Returns([source.SessionId]);
        httpApiService.GetRecordedSessionSourceAsync(source.SessionId).Returns(transfer);

        await CreateService().SyncAll();

        await recordedSessionSourceRepository.DidNotReceive().PutRecordedSessionSourceAsync(Arg.Any<RecordedSessionSource>());
    }

    private static RecordedSessionSource CreateRecordedSource()
    {
        var payload = new byte[] { 8, 6, 7, 5 };
        return new RecordedSessionSource
        {
            SessionId = Guid.NewGuid(),
            SourceKind = RecordedSessionSourceKind.ImportedSst,
            SourceName = "sync.SST",
            SchemaVersion = 1,
            SourceHash = RecordedSessionSourceHash.Compute(
                RecordedSessionSourceKind.ImportedSst,
                "sync.SST",
                1,
                payload),
            Payload = payload
        };
    }

    private static ExtensionSyncEnvelope CreateExtensionEnvelope(string extensionId) => new(
        extensionId,
        SchemaVersion: 1,
        ContentType: "application/test",
        Payload: [1, 2, 3]);

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
            SynchronizationPhase,
            int,
            int,
            CancellationToken,
            Task<IReadOnlyList<SynchronizationProgressSnapshot>>>? ApplyBatchesAsyncOverride { get; init; }

        public Task<List<ExtensionSyncEnvelope>> CreateBatchesAsync(
            long since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateBatchesResult);

        public Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyBatchesAsync(
            IEnumerable<ExtensionSyncEnvelope> envelopes,
            SynchronizationPhase phase,
            int currentStep,
            int totalSteps,
            CancellationToken cancellationToken = default)
        {
            return ApplyBatchesAsyncOverride?.Invoke(envelopes, phase, currentStep, totalSteps, cancellationToken)
                ?? Task.FromResult(ApplyBatchesResult);
        }
    }
}
