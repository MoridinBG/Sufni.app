using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;

namespace Sufni.App.Shared.Stores;

/// <summary>Groups UI-thread cache edits into one notification commit.</summary>
public sealed class StorePublication(IUiThreadDispatcher dispatcher) : IDisposable
{
    private readonly Subject<StorePublicationCommit> committed = new();
    private readonly Dictionary<object, IDisposable> suspended = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> changed = new(ReferenceEqualityComparer.Instance);
    private int depth;

    public IObservable<StorePublicationCommit> Committed => committed.AsObservable();

    /// <summary>Begins a synchronous publication batch. Load and compute before entering it.</summary>
    public IDisposable BeginBatch()
    {
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("Store publication batches must run on the UI thread.");

        depth++;
        return Disposable.Create(EndBatch);
    }

    internal Task MutateAsync(object store, Func<IDisposable> suspendNotifications, Action mutation)
    {
        if (dispatcher.CheckAccess())
        {
            Apply();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(Apply);

        void Apply()
        {
            using var batch = BeginBatch();
            if (!suspended.ContainsKey(store))
                suspended.Add(store, suspendNotifications());
            mutation();
        }
    }

    internal void RecordChange(object store) => changed.Add(store);

    private void EndBatch()
    {
        if (depth > 1)
        {
            depth--;
            return;
        }

        foreach (var suspension in suspended.Values)
            suspension.Dispose();

        var stores = changed.ToImmutableHashSet(ReferenceEqualityComparer.Instance);
        suspended.Clear();
        changed.Clear();
        depth = 0;

        if (stores.Count > 0)
            committed.OnNext(new StorePublicationCommit(stores));
    }

    public void Dispose() => committed.Dispose();
}

/// <summary>Store identities whose notifications have been published together.</summary>
public sealed record StorePublicationCommit(ImmutableHashSet<object> Stores);
