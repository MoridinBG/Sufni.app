using System;
using System.Collections.Generic;
using DynamicData;

namespace Sufni.App.Stores;

// Single source of truth for the runtime live DAQ catalog. The store is
// singleton-backed and mutated by the live coordinator as discovery and
// known-board state are reconciled.
internal sealed class LiveDaqStore : ILiveDaqStoreWriter
{
    private readonly SourceCache<LiveDaqSnapshot, string> source = new(snapshot => snapshot.IdentityKey);

    public IObservable<IChangeSet<LiveDaqSnapshot, string>> Connect() => source.Connect();

    public LiveDaqSnapshot? Get(string identityKey)
    {
        var lookup = source.Lookup(identityKey);
        return lookup.HasValue ? lookup.Value : null;
    }

    public void Upsert(LiveDaqSnapshot snapshot) => source.AddOrUpdate(snapshot);

    public void Remove(string identityKey) => source.RemoveKey(identityKey);

    public void Clear() => source.Clear();

    public void ReplaceAll(IEnumerable<LiveDaqSnapshot> snapshots) =>
        source.Edit(updater => updater.Load(snapshots));
}