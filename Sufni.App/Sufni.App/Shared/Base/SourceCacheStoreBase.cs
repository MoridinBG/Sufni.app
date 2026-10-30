using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DynamicData;
using System.Reactive.Linq;
using Sufni.App.Shared.Stores;

namespace Sufni.App.Shared.Base;

public abstract class SourceCacheStoreBase<TSnapshot, TKey> : IDisposable
    where TSnapshot : class
    where TKey : notnull
{
    private readonly SourceCache<TSnapshot, TKey> source;
    private readonly StorePublication publication;
    private readonly IDisposable publicationSubscription;

    protected SourceCacheStoreBase(Func<TSnapshot, TKey> keySelector, StorePublication publication)
    {
        this.publication = publication;
        source = new SourceCache<TSnapshot, TKey>(keySelector);
        publicationSubscription = source.Connect().Subscribe(_ => publication.RecordChange(this));
    }

    public void Dispose()
    {
        publicationSubscription.Dispose();
        source.Dispose();
    }

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

    protected Task PublishChangesAsync(IReadOnlyCollection<TSnapshot> snapshots, IReadOnlyCollection<TKey> removedKeys) =>
        MutateAsync(() => source.Edit(updater =>
        {
            updater.RemoveKeys(removedKeys);
            updater.AddOrUpdate(snapshots);
        }));

    public Task ReplaceAllAsync(IReadOnlyList<TSnapshot> snapshots) =>
        MutateAsync(() => source.EditDiff(snapshots, EqualityComparer<TSnapshot>.Default));

    private Task MutateAsync(Action mutation) =>
        publication.MutateAsync(this, source.SuspendNotifications, mutation);
}
