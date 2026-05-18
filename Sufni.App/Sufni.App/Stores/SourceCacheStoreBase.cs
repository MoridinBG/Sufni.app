using System;
using System.Collections.Generic;
using DynamicData;

namespace Sufni.App.Stores;

// Shared DynamicData backing for entity stores. Concrete stores layer typed
// lookup/watch APIs on top while coordinators remain the only writers.
internal abstract class SourceCacheStoreBase<TSnapshot, TKey>(Func<TSnapshot, TKey> keySelector)
    where TSnapshot : class
    where TKey : notnull
{
    private readonly SourceCache<TSnapshot, TKey> source = new(keySelector);

    public IObservable<IChangeSet<TSnapshot, TKey>> Connect() => source.Connect();

    public TSnapshot? Get(TKey key)
    {
        var lookup = source.Lookup(key);
        return lookup.HasValue ? lookup.Value : null;
    }

    public void Upsert(TSnapshot snapshot) => source.AddOrUpdate(snapshot);

    public void Remove(TKey key) => source.RemoveKey(key);

    protected IEnumerable<TSnapshot> Items => source.Items;

    protected IObservable<Change<TSnapshot, TKey>> WatchCore(TKey key) => source.Watch(key);

    protected void ReplaceWith(IEnumerable<TSnapshot> snapshots)
    {
        source.Edit(cache =>
        {
            cache.Clear();
            cache.AddOrUpdate(snapshots);
        });
    }
}
