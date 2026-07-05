using SQLite;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.TestSupport.Io;
using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.TestSupport.Fixtures;

/// <summary>
/// Real core persistence/recompute/sync harness for extension integration tests.
/// It lets private extension tests exercise app internals without duplicating
/// SQLite schema or recompute wiring outside the public test-support assembly.
/// </summary>
public sealed class TestRecordedSessionSyncHarness : IAsyncDisposable
{
    private readonly TempDatabase tempDatabase;
    private readonly SqliteConnectionContext context;
    private readonly IProcessingFingerprintService fingerprintService;
    private readonly ITrackRepository trackRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly ISyncDataStore syncDataStore;
    private readonly IRecordedSessionReprocessor reprocessor;

    private TestRecordedSessionSyncHarness(
        TempDatabase tempDatabase,
        IReadOnlyList<IExtensionDatabaseMigrator> extensionMigrators)
    {
        this.tempDatabase = tempDatabase;
        context = new SqliteConnectionContext(
            tempDatabase.DatabasePath,
            createAppDirectories: false,
            extensionMigrators,
            extensionCascadeRuleProviders: [],
            extensionStateRefreshParticipantsProvider: () => []);

        fingerprintService = new ProcessingFingerprintService();
        trackRepository = new TrackRepository(context);
        sessionRepository = new SessionRepository(context, fingerprintService);
        var sessionTelemetryProcessor = new SessionTelemetryProcessor();
        sessionTelemetryWriter = new SessionTelemetryWriter(
            sessionRepository,
            trackRepository,
            sessionTelemetryProcessor);
        recordedSessionSourceRepository = new RecordedSessionSourceRepository(context);
        syncDataStore = new SynchronizationMergeEngine(context, trackRepository, fingerprintService);
        reprocessor = new RecordedSessionReprocessor(
            fingerprintService,
            new TelemetryBikeProcessingContextFactory(
                new RearTravelCalibrationBuilder(new KinematicSolutionCache())));
        ExtensionDatabaseConnection = new ExtensionDatabaseConnection(context);
    }

    public IExtensionDatabaseConnection ExtensionDatabaseConnection { get; }

    public IProcessingFingerprintService FingerprintService => fingerprintService;

    public static async Task<TestRecordedSessionSyncHarness> CreateAsync(
        params IExtensionDatabaseMigrator[] extensionMigrators)
    {
        var tempDatabase = new TempDatabase("recorded-session-sync.db", "sufni-recorded-session-sync-test");
        try
        {
            var harness = new TestRecordedSessionSyncHarness(tempDatabase, extensionMigrators);
            await harness.context.GetInitializedConnectionAsync();
            return harness;
        }
        catch
        {
            tempDatabase.Dispose();
            throw;
        }
    }

    public Task<SQLiteAsyncConnection> GetInitializedConnectionAsync() =>
        context.GetInitializedConnectionAsync();

    public Task<Guid> PutAsync<T>(T item)
        where T : Synchronizable, new() =>
        new SynchronizableRepository<T>(context).PutAsync(item);

    public Task<T?> GetAsync<T>(Guid id)
        where T : Synchronizable, new() =>
        new SynchronizableRepository<T>(context).GetAsync(id);

    public Task DeleteAsync<T>(Guid id)
        where T : Synchronizable, new() =>
        new SynchronizableRepository<T>(context).DeleteAsync(id);

    public Task<Session?> GetSessionAsync(Guid sessionId) =>
        sessionRepository.GetSessionAsync(sessionId);

    public Task<Session> PutProcessedSessionAsync(
        Session session,
        ProcessedTelemetryPayload payload,
        Track? newFullTrack,
        RecordedSessionSource? source) =>
        sessionTelemetryWriter.PutProcessedSessionAsync(session, payload, newFullTrack, source);

    public Task<Session?> UpdateProcessedDerivedDataAsync(
        Session session,
        RecordedSessionReprocessResult result,
        ProcessingFingerprint expectedInputFingerprint) =>
        sessionTelemetryWriter.UpdateProcessedDerivedDataAsync(
            session,
            result.ProcessedTelemetry,
            result.GeneratedFullTrack,
            expectedInputFingerprint);

    public Task PatchSessionPsstAsync(Guid sessionId, SessionBlobPayload payload) =>
        sessionTelemetryWriter.PatchSessionPsstAsync(sessionId, payload.Data, payload.Fingerprint);

    public Task<(byte[] Data, string? Fingerprint)?> GetSessionRawPsstWithFingerprintAsync(Guid sessionId) =>
        sessionRepository.GetSessionRawPsstWithFingerprintAsync(sessionId);

    public async Task<SessionBlobPayload> GetSessionBlobPayloadAsync(Guid sessionId)
    {
        var raw = await sessionRepository.GetSessionRawPsstWithFingerprintAsync(sessionId)
                  ?? throw new InvalidOperationException($"Session '{sessionId}' does not have processed data.");
        return new SessionBlobPayload(raw.Fingerprint, raw.Data);
    }

    public Task PutRecordedSessionSourceAsync(RecordedSessionSource source) =>
        recordedSessionSourceRepository.PutRecordedSessionSourceAsync(source);

    public Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid sessionId) =>
        recordedSessionSourceRepository.GetRecordedSessionSourceAsync(sessionId);

    public async Task<RecordedSessionSourcePayload> GetRecordedSessionSourcePayloadAsync(Guid sessionId)
    {
        var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(sessionId)
                     ?? throw new InvalidOperationException($"Recorded-session source '{sessionId}' was not found.");
        return new RecordedSessionSourcePayload(
            source.SessionId,
            source.SourceKind,
            source.SourceName,
            source.SchemaVersion,
            source.SourceHash,
            source.Payload);
    }

    public Task ApplyRecordedSessionSourcePayloadAsync(RecordedSessionSourcePayload transfer) =>
        recordedSessionSourceRepository.PutRecordedSessionSourceAsync(new RecordedSessionSource
        {
            SessionId = transfer.SessionId,
            SourceKind = transfer.SourceKind,
            SourceName = transfer.SourceName,
            SchemaVersion = transfer.SchemaVersion,
            SourceHash = transfer.SourceHash,
            Payload = transfer.Payload
        });

    public Task<SynchronizationData> GetSynchronizationDataAsync(long since) =>
        syncDataStore.GetSynchronizationDataAsync(since);

    public Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data) =>
        syncDataStore.ApplyRemoteSynchronizationDataAsync(data);

    public Task<RecordedSessionReprocessResult> ReprocessAsync(
        RecordedSessionDomainSnapshot domain,
        RecordedSessionSource source,
        TelemetryProcessingOptions? options = null,
        CancellationToken cancellationToken = default) =>
        options is null
            ? reprocessor.ReprocessAsync(domain, source, cancellationToken)
            : reprocessor.ReprocessAsync(domain, source, options, cancellationToken);

    public async Task<RecordedSessionDomainSnapshot> GetDomainSnapshotAsync(
        Guid sessionId,
        IRecordedSessionDerivationWindowProvider? windowProvider = null,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetSessionAsync(sessionId)
                      ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var setup = session.Setup is { } setupId
            ? await GetAsync<Setup>(setupId)
            : null;
        var bike = setup is not null
            ? await GetAsync<Bike>(setup.BikeId)
            : null;
        var window = windowProvider is null
            ? null
            : await windowProvider.GetWindowAsync(sessionId, cancellationToken);
        var effectiveSourceSessionId = window?.SourceSessionId ?? sessionId;
        var source = await recordedSessionSourceRepository.GetRecordedSessionSourceSnapshotAsync(effectiveSourceSessionId);

        var sessionSnapshot = SessionSnapshot.From(session);
        var setupSnapshot = setup is null ? null : SetupSnapshot.From(setup, boardId: null);
        var bikeSnapshot = bike is null ? null : BikeSnapshot.From(bike);
        var dependencyHash = setupSnapshot is not null && bikeSnapshot is not null
            ? ProcessingDependencyHash.Compute(setupSnapshot, bikeSnapshot)
            : null;
        var evaluation = fingerprintService.EvaluateState(
            sessionSnapshot,
            setupSnapshot,
            bikeSnapshot,
            source,
            dependencyHash,
            window: window);

        return new RecordedSessionDomainSnapshot(
            sessionSnapshot,
            setupSnapshot,
            bikeSnapshot,
            evaluation.Current,
            evaluation.Persisted,
            source,
            window,
            evaluation.Staleness,
            DerivedChangeKind.None,
            dependencyHash);
    }

    public async ValueTask DisposeAsync()
    {
        await context.Connection.CloseAsync();
        tempDatabase.Dispose();
    }
}
