using System.Diagnostics.CodeAnalysis;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionGraph;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Extensibility.Database;
namespace Sufni.App.Tests.TestSupport;

internal sealed class TestPersistenceHarness
{
    private readonly SqliteConnectionContext context;
    private readonly ITrackRepository trackRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionTelemetryProcessor sessionTelemetryProcessor;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly ISyncDataStore syncDataStore;
    private readonly IExtensionDatabaseConnection extensionDatabaseConnection;

    public TestPersistenceHarness(string databasePath)
        : this(CreateConnectionContext(databasePath, []))
    {
    }

    public TestPersistenceHarness(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators)
        : this(CreateConnectionContext(databasePath, extensionMigrators))
    {
    }

    private TestPersistenceHarness(SqliteConnectionContext context)
    {
        this.context = context;
        var fingerprintService = new ProcessingFingerprintService();
        trackRepository = new TrackRepository(context);
        sessionRepository = new SessionRepository(context, fingerprintService);
        sessionTelemetryProcessor = new SessionTelemetryProcessor();
        var sessionCacheStore = new SessionCacheStore(context);
        sessionTelemetryWriter = new SessionTelemetryWriter(sessionRepository, trackRepository, sessionTelemetryProcessor, sessionCacheStore);
        recordedSessionSourceRepository = new RecordedSessionSourceRepository(context);
        syncDataStore = new SynchronizationMergeEngine(context, trackRepository, fingerprintService);
        extensionDatabaseConnection = new ExtensionDatabaseConnection(context);
    }

    public ISessionRepository SessionRepository => sessionRepository;

    public ISessionTelemetryWriter SessionTelemetryWriter => sessionTelemetryWriter;

    public Task<SQLiteAsyncConnection> GetInitializedConnectionAsync() =>
        context.GetInitializedConnectionAsync();

    public Task<IExtensionDatabaseSession> OpenSessionAsync() =>
        extensionDatabaseConnection.OpenSessionAsync();

    public Task<List<T>> GetAllAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        where T : Synchronizable, new() =>
        new SynchronizableRepository<T>(context).GetAllAsync();

    public async Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(long since)
        where T : Synchronizable, new()
    {
        if (typeof(T) == typeof(Session))
        {
            var synchronizationData = await syncDataStore.GetSynchronizationDataAsync(since);
            return (List<T>)(object)synchronizationData.Sessions;
        }

        return await new SynchronizableRepository<T>(context).GetChangedAsync(since);
    }

    public Task<T?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id)
        where T : Synchronizable, new() =>
        new SynchronizableRepository<T>(context).GetAsync(id);

    public Task<Guid> PutAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item)
        where T : Synchronizable, new() =>
        new SynchronizableRepository<T>(context).PutAsync(item);

    public Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item)
        where T : Synchronizable, new() =>
        new SynchronizableRepository<T>(context).DeleteAsync(item);

    public Task<List<Session>> GetSessionsAsync() =>
        sessionRepository.GetSessionsAsync();

    public Task<Session?> GetSessionAsync(Guid id) =>
        sessionRepository.GetSessionAsync(id);

    public Task<List<Guid>> GetIncompleteSessionIdsAsync() =>
        sessionRepository.GetIncompleteSessionIdsAsync();

    public async Task<TelemetryData?> GetSessionPsstAsync(Guid id)
    {
        var raw = await sessionRepository.GetSessionRawPsstAsync(id);
        return raw is null ? null : sessionTelemetryProcessor.ReadProcessedTelemetryData(raw);
    }

    public Task<byte[]?> GetSessionRawPsstAsync(Guid id) =>
        sessionRepository.GetSessionRawPsstAsync(id);

    public Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id) =>
        sessionRepository.GetSessionTrackAsync(id);

    public Task<Guid> PutSessionAsync(Session session) =>
        sessionRepository.PutSessionAsync(session);

    public Task<Session> PutProcessedSessionAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source) =>
        sessionTelemetryWriter.PutProcessedSessionAsync(session, newFullTrack, source);

    public Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        Track? newFullTrack,
        ProcessingFingerprint expectedInputFingerprint) =>
        sessionTelemetryWriter.UpdateProcessedDerivedDataAsync(session, newFullTrack, expectedInputFingerprint);

    public Task PatchSessionPsstAsync(Guid id, byte[] data, string? fingerprint = null) =>
        sessionTelemetryWriter.PatchSessionPsstAsync(id, data, fingerprint);

    public Task SwapSessionPsstAsync(Guid id, byte[] data, string? fingerprint = null) =>
        sessionTelemetryWriter.SwapSessionPsstAsync(id, data, fingerprint);

    public Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points, double? gpsOffsetSeconds = null) =>
        sessionTelemetryWriter.PatchSessionTrackAsync(id, points, gpsOffsetSeconds);

    public Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync() =>
        recordedSessionSourceRepository.GetRecordedSessionSourcesAsync();

    public Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id) =>
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(id);

    public Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync() =>
        recordedSessionSourceRepository.GetSessionIdsMissingRecordedSourceAsync();

    public Task PutRecordedSessionSourceAsync(RecordedSessionSource source) =>
        recordedSessionSourceRepository.PutRecordedSessionSourceAsync(source);

    public Task DeleteRecordedSessionSourceAsync(Guid sessionId) =>
        recordedSessionSourceRepository.DeleteRecordedSessionSourceAsync(sessionId);

    public Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime) =>
        trackRepository.FindTrackByTimeRangeAsync(startTime, endTime);

    public Task<Guid?> FindTrackContainingTimestampAsync(long? timestamp) =>
        trackRepository.FindTrackContainingTimestampAsync(timestamp);

    public Task<long> GetLastSyncTimeAsync(string? serverUrl) =>
        syncDataStore.GetLastSyncTimeAsync(serverUrl);

    public Task UpdateLastSyncTimeAsync(string? serverUrl) =>
        syncDataStore.UpdateLastSyncTimeAsync(serverUrl);

    public Task<SynchronizationData> GetSynchronizationDataAsync(long since) =>
        syncDataStore.GetSynchronizationDataAsync(since);

    public Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data) =>
        syncDataStore.ApplyRemoteSynchronizationDataAsync(data);

    public Task MergeAllAsync(SynchronizationData data) =>
        syncDataStore.MergeAllAsync(data);

    private static SqliteConnectionContext CreateConnectionContext(
        string databasePath,
        IEnumerable<IExtensionDatabaseMigrator> extensionMigrators) =>
        new(
            databasePath,
            createAppDirectories: false,
            extensionMigrators,
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipantsProvider: () => []);
}
