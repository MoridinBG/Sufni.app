using System;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Sessions.Coordination;

internal sealed class SessionPersistenceTransactionRunner(
    SqliteConnectionContext connectionContext,
    IExtensionCascadeService? extensionCascadeService = null)
    : ISessionPersistenceTransactionRunner
{
    public async Task DeleteSessionAsync(
        Guid sessionId,
        Guid? fullTrackId,
        bool deleteFullTrack,
        bool deleteSource,
        CancellationToken cancellationToken = default)
    {
        var rulesApplied = false;
        await connectionContext.RunInTransactionAsync(connection =>
        {
            rulesApplied |= SynchronizableRepository<Session>.DeleteInTransaction(
                connection,
                sessionId,
                extensionCascadeService);

            if (deleteSource)
            {
                RecordedSessionSourceRepository.DeleteRecordedSessionSourceInTransaction(connection, sessionId);
            }

            if (deleteFullTrack && fullTrackId.HasValue)
            {
                rulesApplied |= SynchronizableRepository<Track>.DeleteInTransaction(
                    connection,
                    fullTrackId.Value,
                    extensionCascadeService);
            }
        }, cancellationToken);

        if (rulesApplied && extensionCascadeService is not null)
        {
            await extensionCascadeService.RefreshExtensionStateAsync(cancellationToken);
        }
    }
}
