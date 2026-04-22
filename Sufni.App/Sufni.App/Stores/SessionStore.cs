using System;
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
internal sealed class SessionStore(IDatabaseService databaseService) : ISessionStoreWriter
{
    private readonly SourceCache<SessionSnapshot, Guid> source = new(s => s.Id);

    public IObservable<IChangeSet<SessionSnapshot, Guid>> Connect() => source.Connect();

    public IObservable<SessionSnapshot> Watch(Guid id) =>
        source.Watch(id)
            // Skip Remove events: RefreshAsync clears and repopulates the
            // cache, so a watched session can briefly disappear before the
            // fresh Add arrives. The editor should react only to the current
            // Add/Update snapshot, not to that transient removal.
            .Where(c => c.Reason is ChangeReason.Add or ChangeReason.Update)
            .Select(c => c.Current);

    public SessionSnapshot? Get(Guid id)
    {
        var lookup = source.Lookup(id);
        return lookup.HasValue ? lookup.Value : null;
    }

    public async Task RefreshAsync()
    {
        var sessions = await databaseService.GetSessionsAsync();
        source.Edit(cache =>
        {
            cache.Clear();
            foreach (var session in sessions)
            {
                cache.AddOrUpdate(SessionSnapshot.From(session));
            }
        });
    }

    public void Upsert(SessionSnapshot snapshot) => source.AddOrUpdate(snapshot);

    public void Remove(Guid id) => source.RemoveKey(id);
}
