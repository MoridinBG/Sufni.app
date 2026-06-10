using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public sealed class SessionDeleter
{
    private static readonly ILogger logger = Log.ForContext<SessionDeleter>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly ISynchronizableRepository<Track> trackEntityRepository;
    private readonly ISynchronizableRepository<Session> sessionEntityRepository;
    private readonly ISessionPreferences sessionPreferences;
    private readonly Func<IEditorFactory> editorFactory;
    private readonly IRecordedSessionSourceRepository recordedSessionSourceRepository;
    private readonly IRecordedSessionSourceStoreWriter sourceStore;
    private readonly IExtensionCascadeService? extensionCascadeService;

    public SessionDeleter(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        ISynchronizableRepository<Track> trackEntityRepository,
        ISynchronizableRepository<Session> sessionEntityRepository,
        ISessionPreferences sessionPreferences,
        Func<IEditorFactory> editorFactory,
        IRecordedSessionSourceRepository recordedSessionSourceRepository,
        IRecordedSessionSourceStoreWriter sourceStore,
        IExtensionCascadeService? extensionCascadeService = null)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.trackEntityRepository = trackEntityRepository;
        this.sessionEntityRepository = sessionEntityRepository;
        this.sessionPreferences = sessionPreferences;
        this.editorFactory = editorFactory;
        this.recordedSessionSourceRepository = recordedSessionSourceRepository;
        this.sourceStore = sourceStore;
        this.extensionCascadeService = extensionCascadeService;
    }

    public async Task<SessionDeleteResult> DeleteAsync(Guid sessionId)
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
            await recordedSessionSourceRepository.DeleteRecordedSessionSourceAsync(sessionId);
            sourceStore.Remove(sessionId);

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

        editorFactory().CloseSessionDetail(sessionId);
        sessionStore.Remove(sessionId);
        logger.Information("Session delete completed for {SessionId}", sessionId);
        return new SessionDeleteResult(SessionDeleteOutcome.Deleted);
    }
}
