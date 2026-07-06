using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Coordination;

public sealed class SessionLoader
{
    private static readonly ILogger logger = Log.ForContext<SessionLoader>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionProcessedTelemetryReader processedTelemetryReader;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ITrackCoordinator trackCoordinator;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;

    internal SessionLoader(
        ISessionStoreWriter sessionStore,
        ISessionProcessedTelemetryReader processedTelemetryReader,
        IBackgroundTaskRunner backgroundTaskRunner,
        ITrackCoordinator trackCoordinator,
        ISessionPresentationService sessionPresentationService,
        IRecordedSessionDomainQuery recordedSessionDomainQuery)
    {
        this.sessionStore = sessionStore;
        this.processedTelemetryReader = processedTelemetryReader;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.trackCoordinator = trackCoordinator;
        this.sessionPresentationService = sessionPresentationService;
        this.recordedSessionDomainQuery = recordedSessionDomainQuery;
    }

    public async Task<SessionDetailLoadResult> LoadDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        IProgress<SessionDetailLoadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        logger.Information("Starting session detail load for {SessionId}", sessionId);

        try
        {
            var domain = recordedSessionDomainQuery.Get(sessionId);
            var dampingSpeedCutoffContext = ResolveDampingSpeedCutoffContext(domain);

            logger.Verbose("Loading local telemetry data for session {SessionId}", sessionId);
            progress.Report(SessionDetailLoadProgress.LoadingTelemetryData);
            var telemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
            progress.Report(SessionDetailLoadProgress.CheckingLocalData);
            var recordedSourceMissingOrHashMismatch = IsRecordedSourceMissingOrHashMismatch(domain);
            if (telemetryData is null || recordedSourceMissingOrHashMismatch)
            {
                logger.Warning("Session detail load found incomplete local data for {SessionId}", sessionId);
                return new SessionDetailLoadResult.IncompleteLocalData(
                    sessionId,
                    new MissingSessionData(
                        ProcessedTelemetryBlob: telemetryData is null,
                        RecordedSourceMissingOrHashMismatch: recordedSourceMissingOrHashMismatch));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            logger.Verbose("Resolving track data for session {SessionId}", sessionId);
            progress.Report(SessionDetailLoadProgress.LoadingMapData);
            var trackTask = trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                fullTrackId,
                telemetryData,
                cancellationToken);

            logger.Verbose("Building session presentation data for {SessionId}", sessionId);
            progress.Report(SessionDetailLoadProgress.BuildingSessionPresentation);
            var presentationTask = backgroundTaskRunner.RunAsync(
                () => sessionPresentationService.BuildCachePresentation(
                    telemetryData,
                    dimensions,
                    cancellationToken,
                    dampingSpeedCutoffContext.Cutoffs),
                cancellationToken);

            await Task.WhenAll(trackTask, presentationTask);
            progress.Report(SessionDetailLoadProgress.FinalizingSessionData);
            var trackData = trackTask.Result;
            var cachePresentation = presentationTask.Result with { DampingSpeedCutoffOwner = dampingSpeedCutoffContext.Owner };

            logger.Information("Session detail load completed for {SessionId}", sessionId);
            return new SessionDetailLoadResult.Loaded(
                new SessionDetailData(
                    new SessionTelemetryPresentationData(
                        telemetryData,
                        trackData.FullTrackId,
                        trackData.FullTrackPoints,
                        trackData.TrackPoints,
                        trackData.MediaColumnWidth,
                        cachePresentation.DampingPercentages,
                        cachePresentation.DampingSpeedCutoffs,
                        cachePresentation.DampingSpeedCutoffOwner),
                    cachePresentation));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Session detail load failed for {SessionId}", sessionId);
            return new SessionDetailLoadResult.Failed(e.Message);
        }
    }

    private static bool IsRecordedSourceMissingOrHashMismatch(RecordedSessionDomainSnapshot? domain)
    {
        if (domain is null)
        {
            return false;
        }

        return RecordedSessionSourceCompleteness.IsSourceMissingOrHashMismatch(
            domain.Source?.SourceHash,
            domain.PersistedFingerprint);
    }

    private static (DampingSpeedCutoffs Cutoffs, DampingSpeedCutoffOwner? Owner) ResolveDampingSpeedCutoffContext(
        RecordedSessionDomainSnapshot? domain)
    {
        var bike = domain?.Bike;
        return bike is null
            ? (DampingSpeedCutoffs.Default, null)
            : (bike.DampingSpeedCutoffs, new DampingSpeedCutoffOwner(bike.Id, bike.Updated));
    }

    private Task<TelemetryData?> LoadTelemetryDataAsync(Guid sessionId, CancellationToken cancellationToken) =>
        processedTelemetryReader.GetAsync(sessionId, cancellationToken);
}
