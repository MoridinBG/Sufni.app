using Sufni.App.ExtensionHost.Services;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHost.SessionGraph;
using Sufni.App.ExtensionHosting.Database;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Coordinators;

/// <summary>
/// Owns recorded-session workflows.
/// It opens session detail state, loads desktop and mobile telemetry, saves
/// metadata and live captures, recomputes derived data, deletes sessions, and
/// applies inbound session changes.
/// </summary>
public class SessionCoordinator : ISessionCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SessionCoordinator>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly SessionLoader sessionLoader;
    private readonly SessionSaver sessionSaver;
    private readonly LiveCaptureSaver liveCaptureSaver;
    private readonly ISessionRepository sessionRepository;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly ISynchronizableRepository<Track> trackEntityRepository;
    private readonly ISynchronizableRepository<Session> sessionEntityRepository;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ISessionPreferences sessionPreferences;
    private readonly IShellCoordinator shell;
    private readonly Func<IEditorFactory> editorFactory;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IRecordedSessionDomainQuery recordedSessionDomainQuery;
    private readonly IRecordedSessionReprocessor recordedSessionReprocessor;
    private readonly IExtensionCascadeService? extensionCascadeService;

    public SessionCoordinator(
        ISessionStoreWriter sessionStore,
        SessionLoader sessionLoader,
        SessionSaver sessionSaver,
        LiveCaptureSaver liveCaptureSaver,
        ISessionRepository sessionRepository,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        ISynchronizableRepository<Track> trackEntityRepository,
        ISynchronizableRepository<Session> sessionEntityRepository,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISessionPreferences sessionPreferences,
        IShellCoordinator shell,
        Func<IEditorFactory> editorFactory,
        IRecordedSessionSourceStoreWriter sourceStore,
        IRecordedSessionDomainQuery recordedSessionDomainQuery,
        IRecordedSessionReprocessor recordedSessionReprocessor,
        ISynchronizationServerService? synchronizationServer = null,
        IExtensionCascadeService? extensionCascadeService = null)
    {
        this.sessionStore = sessionStore;
        this.sessionLoader = sessionLoader;
        this.sessionSaver = sessionSaver;
        this.liveCaptureSaver = liveCaptureSaver;
        this.sessionRepository = sessionRepository;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.trackEntityRepository = trackEntityRepository;
        this.sessionEntityRepository = sessionEntityRepository;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.sessionPreferences = sessionPreferences;
        this.shell = shell;
        this.editorFactory = editorFactory;
        this.sourceStore = sourceStore;
        this.recordedSessionDomainQuery = recordedSessionDomainQuery;
        this.recordedSessionReprocessor = recordedSessionReprocessor;
        this.extensionCascadeService = extensionCascadeService;

        if (synchronizationServer is not null)
        {
            synchronizationServer.SynchronizationDataArrived += OnSynchronizationDataArrived;
            synchronizationServer.SessionDataArrived += OnSessionDataArrived;
            synchronizationServer.SessionSourceDataArrived += OnSessionSourceDataArrived;
        }
    }

    public virtual Task OpenEditAsync(Guid sessionId)
    {
        var snapshot = sessionStore.Get(sessionId);
        if (snapshot is null) return Task.CompletedTask;

        shell.OpenOrFocus<SessionDetailViewModel>(
            editor => editor.Id == sessionId,
            () => editorFactory().CreateSessionDetail(snapshot));
        return Task.CompletedTask;
    }

    public virtual Task<SessionDesktopLoadResult> LoadDesktopDetailAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => sessionLoader.LoadDesktopDetailAsync(sessionId, cancellationToken);

    public virtual Task<SessionMobileLoadResult> LoadMobileDetailAsync(
        Guid sessionId,
        SessionPresentationDimensions dimensions,
        CancellationToken cancellationToken = default)
        => sessionLoader.LoadMobileDetailAsync(sessionId, dimensions, cancellationToken);

    public virtual Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated) =>
        sessionSaver.SaveAsync(session, baselineUpdated);

    public virtual Task<LiveSessionSaveResult> SaveLiveCaptureAsync(
        Session session,
        LiveSessionCapturePackage capture,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
        => liveCaptureSaver.SaveLiveCaptureAsync(session, capture, preferences, cancellationToken);

    public virtual async Task<SessionRecomputeResult> RecomputeAsync(
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

    public virtual async Task<SessionDeleteResult> DeleteAsync(Guid sessionId)
    {
        logger.Information("Starting session delete for {SessionId}", sessionId);

        try
        {
            var session = await sessionRepository.GetSessionAsync(sessionId);
            var trackId = session?.FullTrack;
            var shouldDeleteTrack = false;

            if (trackId.HasValue)
            {
                var sessions = await sessionEntityRepository.GetAllAsync();
                shouldDeleteTrack = !sessions.Any(existing => existing.Id != sessionId && existing.FullTrack == trackId);
            }

            await sessionEntityRepository.DeleteAsync(sessionId);
            if (extensionCascadeService is not null)
            {
                await extensionCascadeService.ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Session, sessionId);
            }
            await sourceStore.RemoveAsync(sessionId);

            if (shouldDeleteTrack && trackId.HasValue)
            {
                try
                {
                    await trackEntityRepository.DeleteAsync(trackId.Value);
                    if (extensionCascadeService is not null)
                    {
                        await extensionCascadeService.ApplyForDeletedCoreEntityAsync(ExtensionCoreEntityKind.Track, trackId.Value);
                    }
                }
                catch (Exception e)
                {
                    logger.Warning(e, "Failed to delete orphaned track {TrackId} after deleting session {SessionId}", trackId.Value, sessionId);
                }
            }

            await sessionPreferences.RemoveRecordedAsync(sessionId);
        }
        catch (Exception e)
        {
            logger.Error(e, "Session delete failed for {SessionId}", sessionId);
            return new SessionDeleteResult(SessionDeleteOutcome.Failed, e.Message);
        }

        shell.CloseIfOpen<SessionDetailViewModel>(editor => editor.Id == sessionId, forgetRestoreHistory: true);
        sessionStore.Remove(sessionId);
        logger.Information("Session delete completed for {SessionId}", sessionId);
        return new SessionDeleteResult(SessionDeleteOutcome.Deleted);
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

    private async void OnSynchronizationDataArrived(object? sender, SynchronizationDataArrivedEventArgs e)
    {
        try
        {
            await HandleSynchronizationDataArrivedAsync(e);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound session synchronization data");
        }
    }

    private async Task HandleSynchronizationDataArrivedAsync(SynchronizationDataArrivedEventArgs e)
    {
        var removals = new List<Guid>();
        var upserts = new List<SessionSnapshot>();

        foreach (var session in e.Data.Sessions)
        {
            if (session.Deleted is not null)
            {
                removals.Add(session.Id);
                continue;
            }

            var fresh = await sessionRepository.GetSessionAsync(session.Id);
            if (fresh is not null)
            {
                upserts.Add(SessionSnapshot.From(fresh));
            }
        }

        logger.Verbose(
            "Applying inbound session synchronization with {RemovalCount} removals and {UpsertCount} upserts",
            removals.Count,
            upserts.Count);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var id in removals)
            {
                sessionStore.Remove(id);
            }

            foreach (var snapshot in upserts)
            {
                sessionStore.Upsert(snapshot);
            }
        });
    }

    private async void OnSessionDataArrived(object? sender, SessionDataArrivedEventArgs e)
    {
        try
        {
            await HandleSessionDataArrivedAsync(e);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound session data for {SessionId}", e.SessionId);
        }
    }

    private async Task HandleSessionDataArrivedAsync(SessionDataArrivedEventArgs e)
    {
        logger.Verbose("Applying inbound session data for {SessionId}", e.SessionId);

        var fresh = await sessionRepository.GetSessionAsync(e.SessionId);
        if (fresh is null)
        {
            logger.Verbose("Ignoring inbound session data because session {SessionId} is missing", e.SessionId);
            return;
        }

        var snapshot = SessionSnapshot.From(fresh);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            sessionStore.Upsert(snapshot);
        });
    }

    private async void OnSessionSourceDataArrived(object? sender, SessionDataArrivedEventArgs e)
    {
        try
        {
            await HandleSessionSourceDataArrivedAsync(e);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Failed to apply inbound recorded source for {SessionId}", e.SessionId);
        }
    }

    private async Task HandleSessionSourceDataArrivedAsync(SessionDataArrivedEventArgs e)
    {
        logger.Verbose("Applying inbound recorded source for {SessionId}", e.SessionId);

        var source = await recordedSessionSourceRepository.GetRecordedSessionSourceAsync(e.SessionId);
        if (source is null)
        {
            logger.Verbose("Ignoring inbound recorded source because source {SessionId} is missing", e.SessionId);
            return;
        }

        var snapshot = RecordedSessionSourceSnapshot.From(source);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            sourceStore.Upsert(snapshot);
        });
    }
}

public abstract record SessionSaveResult
{
    private SessionSaveResult() { }

    public sealed record Saved(long NewBaselineUpdated) : SessionSaveResult;
    public sealed record Conflict(SessionSnapshot CurrentSnapshot) : SessionSaveResult;
    public sealed record Failed(string ErrorMessage) : SessionSaveResult;
}

public abstract record LiveSessionSaveResult
{
    private LiveSessionSaveResult() { }

    public sealed record Saved(Guid SessionId, long Updated) : LiveSessionSaveResult;
    public sealed record Failed(string ErrorMessage) : LiveSessionSaveResult;
}

/// <summary>
/// Result of attempting to rebuild a recorded session's derived telemetry.
/// It distinguishes successful recompute, optimistic-concurrency conflict,
/// unrecomputable current state, and failure.
/// </summary>
public abstract record SessionRecomputeResult
{
    private SessionRecomputeResult() { }

    public sealed record Recomputed(long NewBaselineUpdated) : SessionRecomputeResult;
    public sealed record Conflict(SessionSnapshot CurrentSnapshot) : SessionRecomputeResult;
    public sealed record NotRecomputable(SessionStaleness Reason) : SessionRecomputeResult;
    public sealed record Failed(string ErrorMessage) : SessionRecomputeResult;
}

public sealed record SessionDeleteResult(SessionDeleteOutcome Outcome, string? ErrorMessage = null);

public enum SessionDeleteOutcome
{
    Deleted,
    Failed
}
