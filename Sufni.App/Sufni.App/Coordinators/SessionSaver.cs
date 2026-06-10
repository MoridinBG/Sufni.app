using System;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public sealed class SessionSaver
{
    private static readonly ILogger logger = Log.ForContext<SessionSaver>();

    private readonly ISessionStoreWriter sessionStore;
    private readonly ISessionRepository sessionRepository;
    private readonly IShellCoordinator shell;

    public SessionSaver(
        ISessionStoreWriter sessionStore,
        ISessionRepository sessionRepository,
        IShellCoordinator shell)
    {
        this.sessionStore = sessionStore;
        this.sessionRepository = sessionRepository;
        this.shell = shell;
    }

    public async Task<SessionSaveResult> SaveAsync(Session session, long baselineUpdated)
    {
        logger.Information("Starting session save for {SessionId}", session.Id);

        var current = sessionStore.Get(session.Id);
        if (current is not null && current.Updated > baselineUpdated)
        {
            logger.Warning("Session save conflict for {SessionId}", session.Id);
            return new SessionSaveResult.Conflict(current);
        }

        try
        {
            if (current is not null && session.ProcessingFingerprintJson is null)
            {
                session.ProcessingFingerprintJson = current.ProcessingFingerprintJson;
            }

            await sessionRepository.PutSessionAsync(session);
            // Re-fetch via the SQL-computed has_data path so the snapshot's
            // HasProcessedData reflects the current DB state.
            var fresh = await sessionRepository.GetSessionAsync(session.Id);
            if (fresh is null)
            {
                logger.Error("Session save failed because the session disappeared after save for {SessionId}", session.Id);
                return new SessionSaveResult.Failed("Session disappeared after save");
            }
            var saved = SessionSnapshot.From(fresh);
            sessionStore.Upsert(saved);
            shell.GoBack();

            logger.Information("Session save completed for {SessionId}", session.Id);
            return new SessionSaveResult.Saved(saved.Updated);
        }
        catch (Exception e)
        {
            logger.Error(e, "Session save failed for {SessionId}", session.Id);
            return new SessionSaveResult.Failed(e.Message);
        }
    }
}
