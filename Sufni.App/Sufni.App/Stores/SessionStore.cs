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
            // Skip Remove (and Refresh) events: a removed snapshot can carry
            // a stale or null Current value, and the editor should not react
            // to its own session disappearing from the store (the tab close
            // path handles that). The clear-and-reload inside RefreshAsync
            // is also surfaced as Remove → Add, so this filter ensures the
            // editor only reacts to the Add side after the dust settles.
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
