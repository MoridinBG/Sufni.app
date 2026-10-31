using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Models;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Coordination;

public interface ISessionLoader
{
    Task<SessionDetailLoadResult> LoadDetailAsync(
        Guid sessionId,
        IProgress<SessionDetailLoadProgress> progress,
        CancellationToken cancellationToken = default);

    Task<SessionDetailTrackLoadResult> LoadTrackAsync(
        Guid sessionId,
        TelemetryData telemetryData,
        IProgress<SessionDetailLoadProgress> progress,
        CancellationToken cancellationToken = default);
}

public sealed class SessionLoader : ISessionLoader
{
    private static readonly ILogger logger = Log.ForContext<SessionLoader>();

    private readonly ISessionStore sessionStore;
    private readonly ITrackStore trackStore;
    private readonly ISessionProcessedTelemetryReader processedTelemetryReader;
    private readonly ITrackCoordinator trackCoordinator;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;

    internal SessionLoader(
        ISessionStore sessionStore,
        ISessionProcessedTelemetryReader processedTelemetryReader,
        ITrackCoordinator trackCoordinator,
        IRecordedSessionDomainQuery recordedSessionDomainQuery,
        ITrackStore trackStore)
    {
        this.sessionStore = sessionStore;
        this.trackStore = trackStore;
        this.processedTelemetryReader = processedTelemetryReader;
        this.trackCoordinator = trackCoordinator;
        this.recordedSessionDomainQuery = recordedSessionDomainQuery;
    }

    public async Task<SessionDetailLoadResult> LoadDetailAsync(
        Guid sessionId,
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
            var telemetryData = await processedTelemetryReader.GetAsync(sessionId, cancellationToken);
            progress.Report(SessionDetailLoadProgress.CheckingLocalData);
            if (telemetryData is null)
            {
                logger.Warning("Session detail load found incomplete local data for {SessionId}", sessionId);
                return new SessionDetailLoadResult.IncompleteLocalData(
                    sessionId,
                    new MissingSessionData(
                        ProcessedTelemetryBlob: telemetryData is null,
                        RecordedSourceMissing: domain?.Staleness is SessionStaleness.MissingRawSource));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = sessionStore.Get(sessionId);
            var selectedTrack = SessionTrackSelection.Select(trackStore.GetAll(),
                snapshot?.Timestamp ?? telemetryData.Metadata.Timestamp,
                snapshot?.DurationSeconds ?? telemetryData.Metadata.Duration, snapshot?.GpsOffsetSeconds ?? 0,
                snapshot?.WindowStartSeconds ?? 0);
            logger.Information("Session telemetry load completed for {SessionId}", sessionId);
            return new SessionDetailLoadResult.Loaded(
                new SessionDetailData(
                    new SessionTelemetryPresentationData(
                        telemetryData,
                        selectedTrack?.Id,
                        FullTrackPoints: null,
                        TrackPoints: null,
                        MediaColumnWidth: null,
                        dampingSpeedCutoffContext.Cutoffs,
                        dampingSpeedCutoffContext.Owner)));
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

    public async Task<SessionDetailTrackLoadResult> LoadTrackAsync(
        Guid sessionId,
        TelemetryData telemetryData,
        IProgress<SessionDetailLoadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        try
        {
            logger.Verbose("Resolving track data for session {SessionId}", sessionId);
            progress.Report(SessionDetailLoadProgress.LoadingMapData);
            var trackData = await trackCoordinator.LoadSessionTrackAsync(
                sessionId,
                telemetryData,
                cancellationToken);

            progress.Report(SessionDetailLoadProgress.FinalizingSessionData);
            logger.Information("Session detail load completed for {SessionId}", sessionId);
            return new SessionDetailTrackLoadResult.Loaded(trackData);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Session track load failed for {SessionId}", sessionId);
            return new SessionDetailTrackLoadResult.Failed(e.Message);
        }
    }

    private static (DampingSpeedCutoffs Cutoffs, DampingSpeedCutoffOwner? Owner) ResolveDampingSpeedCutoffContext(
        RecordedSessionDomainSnapshot? domain)
    {
        var bike = domain?.Bike;
        return bike is null
            ? (DampingSpeedCutoffs.Default, null)
            : (bike.DampingSpeedCutoffs, new DampingSpeedCutoffOwner(bike.Id, bike.Updated));
    }
}
