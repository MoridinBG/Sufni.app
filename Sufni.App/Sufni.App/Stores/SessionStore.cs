using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Services;

namespace Sufni.App.Stores;

/// <summary>
/// Single source of truth for "what sessions exist" (metadata only —
/// the psst blob lives in the database, not here). Loaded once at
/// startup and updated by coordinators via
/// <see cref="ISessionStoreWriter"/>.
/// </summary>
internal sealed class SessionStore(IDatabaseService databaseService)
    : SourceCacheStoreBase<SessionSnapshot, Guid>(s => s.Id), ISessionStoreWriter
{
    public IObservable<SessionSnapshot> Watch(Guid id) =>
        WatchCore(id)
            // Skip Remove events: RefreshAsync clears and repopulates the
            // cache, so a watched session can briefly disappear before the
            // fresh Add arrives. The editor should react only to the current
            // Add/Update snapshot, not to that transient removal.
            .Where(c => c.Reason is ChangeReason.Add or ChangeReason.Update)
            .Select(c => c.Current);

    public async Task RefreshAsync()
    {
        var sessions = await databaseService.GetSessionsAsync();
        ReplaceWith(sessions.Select(SessionSnapshot.From));
    }
}
