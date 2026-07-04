using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Base;
namespace Sufni.App.Sessions.Store;

/// <summary>
/// Single source of truth for "what sessions exist" (metadata only —
/// the psst blob lives in the database, not here). Loaded once at
/// startup and updated by coordinators via
/// <see cref="ISessionStoreWriter"/>.
/// </summary>
internal sealed class SessionStore(
    ISessionRepository sessionRepository,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<SessionSnapshot, Guid>(s => s.Id, uiThreadDispatcher), ISessionStoreWriter
{
    public IObservable<SessionSnapshot> Watch(Guid id) =>
        WatchCore(id)
            // Skip Remove events: RefreshAsync clears and repopulates the
            // cache, so a watched session can briefly disappear before the
            // fresh Add arrives. The editor should react only to the current
            // Add/Update snapshot, not to that transient removal.
            .Where(c => c.Reason is ChangeReason.Add or ChangeReason.Update)
            .Select(c => c.Current);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await sessionRepository.GetSessionsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceWithAsync(sessions.Select(SessionSnapshot.From));
    }

    public async Task PublishSessionsChangedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<SessionSnapshot>();
        var removedIds = new List<Guid>();

        foreach (var sessionId in sessionIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = await sessionRepository.GetSessionAsync(sessionId);
            if (session is null)
            {
                removedIds.Add(sessionId);
            }
            else
            {
                snapshots.Add(SessionSnapshot.From(session));
            }
        }

        if (snapshots.Count > 0)
        {
            await PublishSnapshotsAsync(snapshots);
        }

        if (removedIds.Count > 0)
        {
            await PublishRemovalsAsync(removedIds);
        }
    }

    public Task PublishSessionsRemovedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(sessionIds.Distinct());
    }

    public void Upsert(SessionSnapshot snapshot) =>
        PublishSnapshotAsync(snapshot).GetAwaiter().GetResult();

    public void Remove(Guid id) =>
        PublishRemoveAsync(id).GetAwaiter().GetResult();
}
