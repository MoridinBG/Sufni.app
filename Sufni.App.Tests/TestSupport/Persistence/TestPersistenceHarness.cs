using System.Diagnostics.CodeAnalysis;
using SQLite;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Segments.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Video.Services;
namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class TestPersistenceHarness : IAsyncDisposable
{
    private readonly SqliteConnectionContext context;
    private readonly TimeProvider timeProvider;
    private readonly ITrackRepository trackRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionTelemetryProcessor sessionTelemetryProcessor;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly ISyncDataStore syncDataStore;
    private readonly ILocalDeviceIdentity localDeviceIdentity;
    private readonly StorePublication publication;

    public TestPersistenceHarness(string databasePath, TimeProvider? timeProvider = null, string writerId = "test-device", bool inMemory = false)
    {
        localDeviceIdentity = PersistenceTestData.CreateDeviceIdentity(writerId);
        this.timeProvider = timeProvider ?? PersistenceTestData.CreateTimeProvider();
        context = PersistenceTestData.CreateConnectionContext(databasePath, this.timeProvider, inMemory: inMemory);
        var fingerprintService = new ProcessingFingerprintService();
        trackRepository = new TrackRepository(context, this.timeProvider, localDeviceIdentity);
        sessionRepository = new SessionRepository(context, this.timeProvider, localDeviceIdentity);
        sessionTelemetryProcessor = new SessionTelemetryProcessor();
        recordedSessionSourceRepository = new RecordedSessionSourceRepository(context);
        var dispatcher = new InlineUiThreadDispatcher();
        publication = new StorePublication(dispatcher);
        PublishedSessions = new SessionStore(publication);
        PublishedSources = new RecordedSessionSourceStore(publication);
        SessionSyncApplier = new SessionSyncApplier(PublishedSessions, sessionRepository, new InlineBackgroundTaskRunner(),
            sessionTelemetryProcessor, new SessionBlobSwapRequestStore(context), fingerprintService);
        syncDataStore = new SynchronizationMergeEngine(context, fingerprintService);
    }

    public async Task SeedGpsMetricsAsync(Guid sessionId, GpsMetrics metrics)
    {
        var connection = await context.GetInitializedConnectionAsync();
        await connection.ExecuteAsync("""
            INSERT OR REPLACE INTO session_gps_metrics (session_id, gps_metrics_key, distance_meters, ascent_meters, descent_meters)
            VALUES (?, '', ?, ?, ?)
            """, sessionId, metrics.DistanceMeters, metrics.AscentMeters, metrics.DescentMeters);
    }

    public ISyncDataStore SyncDataStore => syncDataStore;
    public IPairedDeviceRepository PairedDeviceRepository => new PairedDeviceRepository(context);

    public async ValueTask DisposeAsync()
    {
        VideoPresence.Dispose();
        await context.DisposeAsync();
        PublishedSessions.Dispose();
        PublishedSources.Dispose();
        publication.Dispose();
    }
    public ISessionBlobSwapRequestStore SwapRequests => new SessionBlobSwapRequestStore(context);
    public ISessionRepository SessionRepository => sessionRepository;
    public IRecordedSessionSourceRepository SourceRepository => recordedSessionSourceRepository;
    public ITrackRepository TrackRepository => trackRepository;

    public SessionSyncApplier SessionSyncApplier { get; }
    public SessionStore PublishedSessions { get; }
    public RecordedSessionSourceStore PublishedSources { get; }

    public Sufni.App.Tests.TestSupport.Video.FakeVideoFilePresence VideoPresence { get; } = new();
    public ISessionVideoRepository SessionVideoRepository => new SessionVideoRepository(context, VideoPresence);

    public ISessionSegmentRepository SessionSegmentRepository => new SessionSegmentRepository(context, timeProvider, localDeviceIdentity);

    public ISessionPersistenceTransactionRunner SessionTransactions => new SessionPersistenceTransactionRunner(context, timeProvider, localDeviceIdentity);

    public Task<SQLiteAsyncConnection> GetInitializedConnectionAsync() =>
        context.GetInitializedConnectionAsync();

    public ISynchronizableRepository<T> GetRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        where T : Synchronizable, new() => new SynchronizableRepository<T>(context, timeProvider, localDeviceIdentity);

    public Task<List<T>> GetAllAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        where T : Synchronizable, new() =>
        GetRepository<T>().GetAllAsync();

    public async Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        long sinceExclusive,
        long upperInclusive)
        where T : Synchronizable, new()
    {
        if (typeof(T) == typeof(Session))
        {
            var synchronizationData = await syncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push,
                sinceExclusive,
                upperInclusive);
            return (List<T>)(object)synchronizationData.Sessions;
        }

        return await GetRepository<T>().GetChangedAsync(
            sinceExclusive,
            upperInclusive);
    }

    public Task<T?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id)
        where T : Synchronizable, new() =>
        GetRepository<T>().GetAsync(id);

    public Task<Guid> PutAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item)
        where T : Synchronizable, new() =>
        GetRepository<T>().PutAsync(item);

    public Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item)
        where T : Synchronizable, new() =>
        GetRepository<T>().DeleteAsync(item);

    public Task<List<Session>> GetSessionsAsync() =>
        sessionRepository.GetSessionsAsync();

    public Task<Session?> GetSessionAsync(Guid id) =>
        sessionRepository.GetSessionAsync(id);

    public Task<List<Guid>> GetIncompleteSessionIdsAsync() =>
        sessionRepository.GetIncompleteSessionIdsAsync();

    public Task<List<Guid>> GetRequestedSessionBlobSwapIdsAsync() =>
        new SessionBlobSwapRequestStore(context).GetRequestedSessionIdsAsync();

    public Task<SessionBlobSwap?> GetSessionBlobSwapRequestAsync(Guid sessionId) =>
        new SessionBlobSwapRequestStore(context).GetRequestAsync(sessionId);

    public Task<SessionProcessingInputBundle?> GetProcessingInputBundleAsync(Guid sessionId) =>
        sessionRepository.GetProcessingInputBundleAsync(sessionId);

    public async Task<TelemetryData?> GetSessionPsstAsync(Guid id)
    {
        var raw = await sessionRepository.GetSessionRawPsstAsync(id);
        return raw is null ? null : sessionTelemetryProcessor.ReadProcessedTelemetryData(raw);
    }

    public Task<byte[]?> GetSessionRawPsstAsync(Guid id) =>
        sessionRepository.GetSessionRawPsstAsync(id);

    public Task<Guid> PutSessionAsync(Session session) =>
        sessionRepository.PutSessionAsync(session);

    public async Task PutSessionWithRecordedSourceAsync(Session session, RecordedSessionSource source)
    {
        await PutSessionAsync(session);
        await PutRecordedSessionSourceAsync(source);
    }

    public async Task<Session> PutProcessedSessionAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source)
    {
        var prepared = PrepareGeneration(session, newFullTrack);
        if (newFullTrack is not null)
        {
            await PutAsync(newFullTrack);
        }
        await PutAsync(prepared);
        if (source is not null) await recordedSessionSourceRepository.PutRecordedSessionSourceAsync(source);
        return (await sessionRepository.GetSessionAsync(prepared.Id))!;
    }

    public Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        Track? newFullTrack,
        SessionProcessingInputBundle expectedInputs) =>
        sessionRepository.UpdateProcessedDerivedDataAsync(PrepareGeneration(session, newFullTrack), newFullTrack, expectedInputs);

    public async Task<SessionDataApplyResult> SwapSessionPsstAsync(Guid id, byte[] data, string? fingerprint = null,
        SessionProcessedGeneration? generation = null)
    {
        var expected = SessionSnapshot.From((await sessionRepository.GetSessionAsync(id))!);
        return await SessionSyncApplier.ApplyDownloadedAsync(expected, new SessionBlobPayload(fingerprint, data), fingerprint, generation);
    }

    public Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id) =>
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);

    public Task<List<RecordedSessionSourceSnapshot>> GetRecordedSessionSourceSnapshotsAsync() =>
        recordedSessionSourceRepository.GetRecordedSessionSourceSnapshotsAsync();

    public Task<RecordedSessionSourceSnapshot?> GetRecordedSessionSourceSnapshotAsync(Guid id) =>
        recordedSessionSourceRepository.GetRecordedSessionSourceSnapshotAsync(id);

    public Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync() =>
        recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync();

    public Task<List<Guid>> GetMissingRecordingSourceIdsAsync() =>
        recordedSessionSourceRepository.GetMissingRecordingSourceIdsAsync();

    public Task<List<Guid>> GetReferencedSourceSessionIdsAsync() =>
        recordedSessionSourceRepository.GetReferencedSourceSessionIdsAsync();

    public Task PutRecordedSessionSourceAsync(RecordedSessionSource source) =>
        recordedSessionSourceRepository.PutRecordedSessionSourceAsync(source);

    public Task<IReadOnlyList<Guid>> DeleteOrphanedRecordedSessionSourcesAsync() =>
        recordedSessionSourceRepository.DeleteOrphanedRecordedSessionSourcesAsync();

    public Task<TrackPayloadMetadata?> GetTrackPayloadMetadataAsync(Guid trackId) =>
        trackRepository.GetTrackPayloadMetadataAsync(trackId);

    public Task<TrackPayload?> GetTrackPayloadAsync(Guid trackId, long updated) =>
        trackRepository.GetTrackPayloadAsync(trackId, updated);

    public Task<long> GetLastPushSequenceAsync(PeerIdentity peer) =>
        syncDataStore.GetLastPushSequenceAsync(peer);

    public Task<long> GetLastPullSequenceAsync(PeerIdentity peer) =>
        syncDataStore.GetLastPullSequenceAsync(peer);

    public Task UpdateLastPushSequenceAsync(PeerIdentity peer, long upperBound) =>
        syncDataStore.UpdateLastPushSequenceAsync(peer, upperBound);

    public Task UpdateLastPullSequenceAsync(PeerIdentity peer, long upperBound) =>
        syncDataStore.UpdateLastPullSequenceAsync(peer, upperBound);

    public Task<SynchronizationData> GetSynchronizationDataAsync(
        long sinceExclusive,
        long upperInclusive, SynchronizationDirection direction = SynchronizationDirection.Push) =>
        syncDataStore.GetSynchronizationDataAsync(direction, sinceExclusive, upperInclusive);

    public Task<long> GetSequenceUpperBoundAsync(SynchronizationDirection direction) => syncDataStore.GetSequenceUpperBoundAsync(direction);

    public Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data) =>
        syncDataStore.ApplyRemoteSynchronizationDataAsync(data);

    public async Task<IReadOnlyList<SessionBlobSwap>> ApplyRemoteSynchronizationDataAndReturnSwapsAsync(SynchronizationData data) =>
        (await syncDataStore.ApplyRemoteSynchronizationDataAsync(data)).Swaps;

    public Task MergeAllAsync(SynchronizationData data) =>
        syncDataStore.MergeAllAsync(data);

    private Session PrepareGeneration(Session session, Track? newFullTrack) =>
        ProcessedGeneration.Create(CreatePayload(session), newFullTrack?.Points)
            .CreateSession(SessionSnapshot.From(session));

    private ProcessedTelemetryPayload CreatePayload(Session session)
    {
        var data = session.ProcessedData ?? throw new InvalidDataException("Processed session data is required.");
        return new ProcessedTelemetryPayload(
            sessionTelemetryProcessor.ReadProcessedTelemetryData(data),
            data,
            session.ProcessingFingerprintJson);
    }
}
