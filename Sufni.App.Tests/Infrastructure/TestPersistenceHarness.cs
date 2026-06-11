using System.Diagnostics.CodeAnalysis;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Infrastructure;

internal sealed class TestPersistenceHarness
{
    private readonly SqliteConnectionContext context;
    private readonly ITrackRepository trackRepository;
    private readonly ISessionRepository sessionRepository;
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
        trackRepository = new TrackRepository(context);
        sessionRepository = new SessionRepository(context, new SessionTelemetryProcessor(), trackRepository);
        recordedSessionSourceRepository = new RecordedSessionSourceRepository(context);
        syncDataStore = new SynchronizationMergeEngine(context, trackRepository);
        extensionDatabaseConnection = new ExtensionDatabaseConnection(context);
    }

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

    public Task<TelemetryData?> GetSessionPsstAsync(Guid id) =>
        sessionRepository.GetSessionPsstAsync(id);

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
        sessionRepository.PutProcessedSessionAsync(session, newFullTrack, source);

    public Task<Session?> PutProcessedSessionIfUnchangedAsync(
        Session session,
        Track? newFullTrack,
        RecordedSessionSource? source,
        long baselineUpdated) =>
        sessionRepository.PutProcessedSessionIfUnchangedAsync(session, newFullTrack, source, baselineUpdated);

    public Task PatchSessionPsstAsync(Guid id, byte[] data) =>
        sessionRepository.PatchSessionPsstAsync(id, data);

    public Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points) =>
        sessionRepository.PatchSessionTrackAsync(id, points);

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

    public Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId) =>
        trackRepository.AssociateSessionWithTrackAsync(sessionId);

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
