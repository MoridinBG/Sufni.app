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
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Sessions.Coordination;

public sealed class SessionLoader
{
    private static readonly ILogger logger = Log.ForContext<SessionLoader>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISessionTelemetryWriter sessionTelemetryWriter;
    private readonly ISessionTelemetryProcessor sessionTelemetryProcessor;
    private readonly ISessionCacheStore sessionCacheStore;
    private readonly IHttpApiService httpApiService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ITrackCoordinator trackCoordinator;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;

    internal SessionLoader(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ISessionTelemetryWriter sessionTelemetryWriter,
        ISessionTelemetryProcessor sessionTelemetryProcessor,
        ISessionCacheStore sessionCacheStore,
        IHttpApiService httpApiService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ITrackCoordinator trackCoordinator,
        ISessionPresentationService sessionPresentationService,
        IRecordedSessionDomainQuery recordedSessionDomainQuery)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.sessionTelemetryWriter = sessionTelemetryWriter;
        this.sessionTelemetryProcessor = sessionTelemetryProcessor;
        this.sessionCacheStore = sessionCacheStore;
        this.httpApiService = httpApiService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.trackCoordinator = trackCoordinator;
        this.sessionPresentationService = sessionPresentationService;
        this.recordedSessionDomainQuery = recordedSessionDomainQuery;
    }

    public async Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting desktop session load for {SessionId}", sessionId);

        try
        {
            logger.Verbose("Loading telemetry data for desktop session {SessionId}", sessionId);
            var telemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
            if (telemetryData is null)
            {
                if (sessionStore.Get(sessionId) is { HasProcessedData: true })
                {
                    logger.Error(
                        "Desktop session load failed because telemetry data was marked present but could not be read for {SessionId}",
                        sessionId);
                    return new SessionDesktopLoadResult.Failed("Session data is marked as present but could not be read.");
                }

                logger.Warning("Desktop session load is waiting for telemetry data for {SessionId}", sessionId);
                return new SessionDesktopLoadResult.TelemetryPending();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            logger.Verbose("Resolving track data for desktop session {SessionId}", sessionId);
            var trackData = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                fullTrackId,
                telemetryData,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var dampingSpeedCutoffContext = ResolveDampingSpeedCutoffContext(sessionId);
            logger.Verbose("Calculating presentation data for desktop session {SessionId}", sessionId);
            var dampingPercentages = await backgroundTaskRunner.RunAsync(
                () => sessionPresentationService.CalculateDampingPercentages(
                    telemetryData,
                    dampingSpeedCutoffs: dampingSpeedCutoffContext.Cutoffs),
                cancellationToken);

            logger.Information("Desktop session load completed for {SessionId}", sessionId);
            return new SessionDesktopLoadResult.Loaded(
                new SessionTelemetryPresentationData(
                    telemetryData,
                    trackData.FullTrackId,
                    trackData.FullTrackPoints,
                    trackData.TrackPoints,
                    trackData.MediaColumnWidth,
                    dampingPercentages,
                    dampingSpeedCutoffContext.Cutoffs,
                    dampingSpeedCutoffContext.Owner));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Desktop session load failed for {SessionId}", sessionId);
            return new SessionDesktopLoadResult.Failed(e.Message);
        }
    }

    public async Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting mobile session load for {SessionId}", sessionId);

        try
        {
            var dampingSpeedCutoffContext = ResolveDampingSpeedCutoffContext(sessionId);

            logger.Verbose("Checking cached mobile presentation for session {SessionId}", sessionId);
            var cached = await backgroundTaskRunner.RunAsync(
                () => sessionCacheStore.GetSessionCacheAsync(sessionId),
                cancellationToken);
            if (cached is not null)
            {
                logger.Verbose("Mobile session cache hit for {SessionId}", sessionId);
                var cachedTelemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
                SessionTrackPresentationData? cachedTrackData = null;
                if (cachedTelemetryData is null)
                {
                    logger.Warning("Mobile session cache is present but local telemetry could not be read for {SessionId}", sessionId);
                }
                else
                {
                    var cachedFullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
                    logger.Verbose("Resolving cached mobile track data for session {SessionId}", sessionId);
                    cachedTrackData = await trackCoordinator.LoadSessionTrackAsync(
                        sessionId,
                        cachedFullTrackId,
                        cachedTelemetryData,
                        cancellationToken);
                }

                logger.Information("Mobile session load completed from cache for {SessionId}", sessionId);
                var cachedPresentation = SessionCachePresentationData.FromCache(cached) with
                {
                    DampingSpeedCutoffOwner = dampingSpeedCutoffContext.Owner,
                };
                if (cachedTelemetryData is not null && cachedPresentation.DampingSpeedCutoffs != dampingSpeedCutoffContext.Cutoffs)
                {
                    logger.Verbose("Recomputing cached damping percentages for session {SessionId} because bike cutoffs changed", sessionId);
                    var dampingPercentages = await backgroundTaskRunner.RunAsync(
                        () => sessionPresentationService.CalculateDampingPercentages(
                            cachedTelemetryData,
                            dampingSpeedCutoffs: dampingSpeedCutoffContext.Cutoffs),
                        cancellationToken);
                    cachedPresentation = cachedPresentation with
                    {
                        DampingPercentages = dampingPercentages,
                        DampingSpeedCutoffs = dampingSpeedCutoffContext.Cutoffs,
                    };
                }

                return new SessionMobileLoadResult.LoadedFromCache(
                    cachedPresentation,
                    cachedTelemetryData,
                    cachedTrackData);
            }

            logger.Verbose("Mobile session cache miss for {SessionId}", sessionId);
            var telemetryData = await EnsureTelemetryDataAvailableForLoadAsync(sessionId, cancellationToken);
            if (telemetryData is null)
            {
                logger.Warning("Mobile session load is waiting for telemetry data for {SessionId}", sessionId);
                return new SessionMobileLoadResult.TelemetryPending();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var mobileFullTrackId = sessionStore.Get(sessionId)?.FullTrackId;
            logger.Verbose("Resolving track data for mobile session {SessionId}", sessionId);
            var trackData = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                mobileFullTrackId,
                telemetryData,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            logger.Verbose("Building mobile session cache for {SessionId}", sessionId);
            var presentation = await backgroundTaskRunner.RunAsync(
                () => sessionPresentationService.BuildCachePresentation(
                    telemetryData,
                    dimensions,
                    cancellationToken,
                    dampingSpeedCutoffContext.Cutoffs),
                cancellationToken);

            presentation = presentation with { DampingSpeedCutoffOwner = dampingSpeedCutoffContext.Owner };

            cancellationToken.ThrowIfCancellationRequested();

            logger.Verbose("Persisting mobile session cache for {SessionId}", sessionId);
            await backgroundTaskRunner.RunAsync(
                () => sessionCacheStore.PutSessionCacheAsync(presentation.ToCache(sessionId)),
                cancellationToken);

            logger.Information("Mobile session load completed for {SessionId}", sessionId);
            return new SessionMobileLoadResult.BuiltCache(presentation, telemetryData, trackData);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Mobile session load failed for {SessionId}", sessionId);
            return new SessionMobileLoadResult.Failed(e.Message);
        }
    }

    private (DampingSpeedCutoffs Cutoffs, DampingSpeedCutoffOwner? Owner) ResolveDampingSpeedCutoffContext(Guid sessionId)
    {
        var bike = recordedSessionDomainQuery.Get(sessionId)?.Bike;
        return bike is null
            ? (DampingSpeedCutoffs.Default, null)
            : (bike.DampingSpeedCutoffs, new DampingSpeedCutoffOwner(bike.Id, bike.Updated));
    }

    private Task<TelemetryData?> LoadTelemetryDataAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return backgroundTaskRunner.RunAsync<TelemetryData?>(
            async () =>
            {
                var raw = await sessionRepository.GetSessionRawPsstAsync(sessionId);
                return raw is null ? null : sessionTelemetryProcessor.ReadProcessedTelemetryData(raw);
            },
            cancellationToken);
    }

    private async Task<TelemetryData?> EnsureTelemetryDataAvailableForLoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var telemetryData = await LoadTelemetryDataAsync(sessionId, cancellationToken);
        if (telemetryData is not null)
        {
            logger.Verbose("Telemetry data cache hit for session {SessionId}", sessionId);
            return telemetryData;
        }

        var current = sessionStore.Get(sessionId);
        if (current is { HasProcessedData: true })
        {
            throw new InvalidOperationException("Session data is marked as present but could not be read.");
        }

        logger.Verbose("Downloading telemetry data during load for session {SessionId}", sessionId);
        var transfer = await httpApiService.GetSessionPsstAsync(sessionId);
        if (transfer is null)
        {
            logger.Warning("Telemetry data is not yet available from the server for session {SessionId}", sessionId);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Commit the downloaded bytes with their fingerprint so the row coherently
        // advertises what it now holds. The fingerprint is written as-is
        // here; the sync phase-4 pull is the path that match-checks against a target.
        await backgroundTaskRunner.RunAsync(
            () => sessionTelemetryWriter.SwapSessionPsstAsync(sessionId, transfer.Data, transfer.Fingerprint),
            cancellationToken);

        var fresh = await backgroundTaskRunner.RunAsync(
            () => sessionRepository.GetSessionAsync(sessionId),
            cancellationToken);
        if (fresh is not null)
        {
            sessionStore.Upsert(SessionSnapshot.From(fresh));
        }

        logger.Verbose("Reloading telemetry data after server download for session {SessionId}", sessionId);
        return await LoadTelemetryDataAsync(sessionId, cancellationToken);
    }
}
