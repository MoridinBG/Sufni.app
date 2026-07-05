using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.Shared.Base;

// Shared DynamicData backing for entity stores. Concrete stores layer typed
// lookup/watch APIs on top while store writers own cache publication.
internal abstract class SourceCacheStoreBase<TSnapshot, TKey>(
    Func<TSnapshot, TKey> keySelector,
    IUiThreadDispatcher uiThreadDispatcher)
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

    protected IEnumerable<TSnapshot> Items => source.Items;

    protected IObservable<Change<TSnapshot, TKey>> WatchCore(TKey key) => source.Watch(key);

    protected Task PublishSnapshotAsync(TSnapshot snapshot) =>
        MutateAsync(() => source.AddOrUpdate(snapshot));

    protected Task PublishSnapshotsAsync(IEnumerable<TSnapshot> snapshots)
    {
        var snapshotList = snapshots.ToArray();
        return MutateAsync(() => source.AddOrUpdate(snapshotList));
    }

    protected Task PublishRemoveAsync(TKey key) =>
        MutateAsync(() => source.RemoveKey(key));

    protected Task PublishRemovalsAsync(IEnumerable<TKey> keys)
    {
        var keyList = keys.ToArray();
        return MutateAsync(() => source.RemoveKeys(keyList));
    }

    protected Task ReplaceWithAsync(IEnumerable<TSnapshot> snapshots)
    {
        var snapshotList = snapshots.ToArray();
        return MutateAsync(() => source.EditDiff(snapshotList, EqualityComparer<TSnapshot>.Default));
    }

    private Task MutateAsync(Action mutation)
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            mutation();
            return Task.CompletedTask;
        }

        return uiThreadDispatcher.InvokeAsync(mutation);
    }
}
