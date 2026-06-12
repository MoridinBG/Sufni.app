using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public sealed class LiveCaptureSaver
{
    private static readonly ILogger logger = Log.ForContext<LiveCaptureSaver>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISynchronizableRepository<Setup> setupRepository;
    private readonly ISynchronizableRepository<Bike> bikeRepository;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;

    public LiveCaptureSaver(
        ISessionStoreWriter sessionStore,
        ISynchronizableRepository<Setup> setupRepository,
        ISynchronizableRepository<Bike> bikeRepository,
        ISessionTelemetryWriter sessionTelemetryWriter,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionPreferences sessionPreferences,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionReprocessor recordedSessionReprocessor)
    {
        this.sessionStore = sessionStore;
        this.setupRepository = setupRepository;
        this.bikeRepository = bikeRepository;
        this.sessionTelemetryWriter = sessionTelemetryWriter;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.sourceStore = sourceStore;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
    }

    public async Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        logger.Information("Starting live session save for {SessionId}", session.Id);

        try
        {
            var processingOptions = preferences.Processing.ToTelemetryProcessingOptions();
            var source = RecordedSessionSourceFactory.CreateLiveCapture(session.Id, capture.TelemetryCapture);
            var setup = await setupRepository.GetAsync(capture.Context.SetupId)
                        ?? throw new InvalidOperationException("Setup is missing.");
            var bike = await bikeRepository.GetAsync(setup.BikeId)
                       ?? throw new InvalidOperationException("Bike is missing.");
            var setupSnapshot = SetupSnapshot.From(setup, boardId: null);
            var bikeSnapshot = BikeSnapshot.From(bike);
            var sourceSnapshot = RecordedSessionSourceSnapshot.From(source);
            var sessionSnapshot = SessionSnapshot.From(session);
            var domain = new RecordedSessionDomainSnapshot(
                sessionSnapshot,
                setupSnapshot,
                bikeSnapshot,
                CurrentFingerprint: null,
                PersistedFingerprint: null,
                sourceSnapshot,
                new SessionStaleness.UnknownLegacyFingerprint(),
                DerivedChangeKind.None);

            var reprocessResult = await backgroundTaskRunner.RunAsync(
                () => recordedSessionReprocessor.ReprocessAsync(domain, source, processingOptions, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            session.ProcessedData = reprocessResult.TelemetryData.BinaryForm;
            session.ProcessingFingerprintJson = AppJson.Serialize(reprocessResult.Fingerprint);

            var fresh = await sessionTelemetryWriter.PutProcessedSessionAsync(session, reprocessResult.GeneratedFullTrack, source);

            var snapshot = SessionSnapshot.From(fresh);
            await sessionPreferences.UpdateRecordedAsync(snapshot.Id, _ => preferences);

            sessionStore.Upsert(snapshot);
            sourceStore.Upsert(sourceSnapshot);

            logger.Information("Live session save completed for {SessionId}", session.Id);
            return new LiveSessionSaveResult.Saved(snapshot.Id, snapshot.Updated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Live session save failed for {SessionId}", session.Id);
            return new LiveSessionSaveResult.Failed(e.Message);
        }
    }
}
