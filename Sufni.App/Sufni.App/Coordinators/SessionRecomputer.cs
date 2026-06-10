using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.Services;
using Sufni.App.ExtensionHost.SessionGraph;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.SessionGraph;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public sealed class SessionRecomputer
{
    private static readonly ILogger logger = Log.ForContext<SessionRecomputer>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISynchronizableRepository<Track> trackEntityRepository;
    private readonly ISynchronizableRepository<Session> sessionEntityRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;
    private readonly IExtensionCascadeService? extensionCascadeService;

    public SessionRecomputer(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ISynchronizableRepository<Track> trackEntityRepository,
        ISynchronizableRepository<Session> sessionEntityRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionPreferences sessionPreferences,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionDomainQuery recordedSessionDomainQuery,
        IRecordedSessionReprocessor recordedSessionReprocessor,
        IExtensionCascadeService? extensionCascadeService = null)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.trackEntityRepository = trackEntityRepository;
        this.sessionEntityRepository = sessionEntityRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.sourceStore = sourceStore;
        this.recordedSessionDomainQuery = recordedSessionDomainQuery;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
        this.extensionCascadeService = extensionCascadeService;
    }

    public async Task<SessionRecomputeResult> RecomputeAsync(
        Guid sessionId,
        long baselineUpdated,
        CancellationToken cancellationToken = default)
    {
        logger.Information("Starting recorded session recompute for {SessionId}", sessionId);

        try
        {
            var domain = recordedSessionDomainQuery.Get(sessionId);
            if (domain is null)
            {
                logger.Warning("Recorded session recompute failed because session {SessionId} is missing", sessionId);
                return new SessionRecomputeResult.Failed("Session is missing.");
            }

            if (domain.Session.Updated > baselineUpdated)
            {
                logger.Warning("Recorded session recompute conflict for {SessionId}", sessionId);
                return new SessionRecomputeResult.Conflict(domain.Session);
            }

            if (!domain.Staleness.CanManualRecompute)
            {
                logger.Warning("Recorded session {SessionId} is not recomputable because {Reason}", sessionId, domain.Staleness.GetType().Name);
                return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
            }

            var source = await sourceStore.LoadAsync(sessionId, cancellationToken);
            if (source is null)
            {
                logger.Warning("Recorded session recompute failed because source {SessionId} is missing", sessionId);
                return new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource());
            }

            var loadedSourceSnapshot = RecordedSessionSourceSnapshot.From(source);
            if (domain.Source != loadedSourceSnapshot)
            {
                sourceStore.Upsert(loadedSourceSnapshot);
                domain = recordedSessionDomainQuery.Get(sessionId);
                if (domain is null)
                {
                    logger.Warning("Recorded session recompute failed because session {SessionId} disappeared after source refresh", sessionId);
                    return new SessionRecomputeResult.Failed("Session is missing.");
                }

                if (domain.Session.Updated > baselineUpdated)
                {
                    logger.Warning("Recorded session recompute conflict for {SessionId} after source refresh", sessionId);
                    return new SessionRecomputeResult.Conflict(domain.Session);
                }

                if (!domain.Staleness.CanManualRecompute)
                {
                    logger.Warning("Recorded session {SessionId} is not recomputable after source refresh because {Reason}", sessionId, domain.Staleness.GetType().Name);
                    return new SessionRecomputeResult.NotRecomputable(domain.Staleness);
                }
            }

            var preferences = await sessionPreferences.GetRecordedAsync(sessionId);
            var processingOptions = preferences.Processing.ToTelemetryProcessingOptions();
            var reprocessResult = await backgroundTaskRunner.RunAsync(
                () => recordedSessionReprocessor.ReprocessAsync(domain, source, processingOptions, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var persisted = await sessionRepository.GetSessionAsync(sessionId);
            if (persisted is null)
            {
                logger.Warning("Recorded session recompute failed because session {SessionId} disappeared before persistence", sessionId);
                return new SessionRecomputeResult.Failed("Session is missing.");
            }

            if (persisted.Updated > baselineUpdated)
            {
                var current = SessionSnapshot.From(persisted);
                logger.Warning("Recorded session recompute conflict for {SessionId} before persistence", sessionId);
                return new SessionRecomputeResult.Conflict(current);
            }

            var previousFullTrackId = persisted.FullTrack;
            var previousFullTrack = previousFullTrackId.HasValue
                ? await trackEntityRepository.GetAsync(previousFullTrackId.Value)
                : null;
            Track? newFullTrack = null;

            if (reprocessResult.GeneratedFullTrack is null)
            {
                persisted.FullTrack = previousFullTrackId;
                persisted.Track = null;
            }
            else if (previousFullTrack is not null &&
                     TrackContentHash.PointsEqual(previousFullTrack, reprocessResult.GeneratedFullTrack))
            {
                persisted.FullTrack = previousFullTrackId;
            }
            else
            {
                newFullTrack = reprocessResult.GeneratedFullTrack;
                persisted.Track = null;
            }

            persisted.ProcessedData = reprocessResult.TelemetryData.BinaryForm;
            persisted.ProcessingFingerprintJson = AppJson.Serialize(reprocessResult.Fingerprint);

            var fresh = await sessionRepository.PutProcessedSessionIfUnchangedAsync(
                persisted,
                newFullTrack,
                source: null,
                baselineUpdated);
            if (fresh is null)
            {
                var current = await sessionRepository.GetSessionAsync(sessionId);
                if (current is null)
                {
                    return new SessionRecomputeResult.Failed("Session is missing.");
                }

                return new SessionRecomputeResult.Conflict(SessionSnapshot.From(current));
            }

            var snapshot = SessionSnapshot.From(fresh);
            sessionStore.Upsert(snapshot);

            await DeletePreviousFullTrackIfOrphanedAsync(previousFullTrackId, fresh.FullTrack, sessionId);

            logger.Information("Recorded session recompute completed for {SessionId}", sessionId);
            return new SessionRecomputeResult.Recomputed(snapshot.Updated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Recorded session recompute failed for {SessionId}", sessionId);
            return new SessionRecomputeResult.Failed(e.Message);
        }
    }

    private async Task DeletePreviousFullTrackIfOrphanedAsync(Guid? previousFullTrackId, Guid? currentFullTrackId, Guid sessionId)
    {
        if (!previousFullTrackId.HasValue || previousFullTrackId == currentFullTrackId)
        {
            return;
        }

        var sessions = await sessionEntityRepository.GetAllAsync();
        var stillReferenced = sessions.Any(existing =>
            existing.Id != sessionId &&
            existing.Deleted is null &&
            existing.FullTrack == previousFullTrackId.Value);
        if (stillReferenced)
        {
            return;
        }

        try
        {
            await trackEntityRepository.DeleteAsync(previousFullTrackId.Value);
            if (extensionCascadeService is not null)
            {
                await extensionCascadeService.ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Track, previousFullTrackId.Value);
            }
        }
        catch (Exception e)
        {
            logger.Warning(e, "Failed to delete orphaned track {TrackId} after recomputing session {SessionId}", previousFullTrackId.Value, sessionId);
        }
    }
}
