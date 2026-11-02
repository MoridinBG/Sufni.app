using System;
using System.Collections.Generic;
using System.Linq;
using DynamicData;
using Sufni.App.Infrastructure;

namespace Sufni.App.LiveDaq.Stores;

// Single source of truth for the runtime live DAQ catalog. The store is
// singleton-backed and mutated by the live coordinator as discovery and
// known-board state are reconciled.
internal sealed class LiveDaqStore(IUiThreadDispatcher dispatcher) : ILiveDaqStoreWriter, IDisposable
{
    private readonly SourceCache<LiveDaqSnapshot, string> source = new(snapshot => snapshot.IdentityKey);
    private bool disposed;

    public IObservable<IChangeSet<LiveDaqSnapshot, string>> Connect() => source.Connect();

    public LiveDaqSnapshot? Get(string identityKey)
    {
        var lookup = source.Lookup(identityKey);
        return lookup.HasValue ? lookup.Value : null;
    }

    public void ReplaceAll(IEnumerable<LiveDaqSnapshot> snapshots)
    {
        var prepared = snapshots.ToArray();
        // Queue every reconciliation in order, including callers already on the UI thread.
        dispatcher.Post(() =>
        {
            if (!disposed) source.Edit(updater => updater.Load(prepared));
        });
    }

    public void Dispose()
    {
        disposed = true;
        source.Dispose();
    }
}
