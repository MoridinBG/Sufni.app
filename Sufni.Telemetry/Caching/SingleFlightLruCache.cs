using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.Telemetry.Caching;

public sealed class SingleFlightLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int? maximumCount;
    private readonly long? maximumWeight;
    private readonly Func<TValue, long>? valueWeight;
    private readonly Func<TValue, bool>? shouldRetain;
    private readonly Func<TKey, TValue>? factory;
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<TKey, LinkedListNode<RetainedEntry>> retained = [];
    private readonly LinkedList<RetainedEntry> lru = [];
    private readonly Dictionary<TKey, Lazy<TValue>> pendingSync = [];
    private readonly Dictionary<TKey, Lazy<Task<TValue>>> pendingAsync = [];
    private long retainedWeight;

    public SingleFlightLruCache(int capacity)
        : this(maximumCount: capacity, maximumWeight: null, valueWeight: null, shouldRetain: null, factory: null)
    {
    }

    public SingleFlightLruCache(int capacity, Func<TKey, TValue> factory)
        : this(
            maximumCount: capacity,
            maximumWeight: null,
            valueWeight: null,
            shouldRetain: null,
            factory: factory ?? throw new ArgumentNullException(nameof(factory)))
    {
    }

    public SingleFlightLruCache(
        long maximumWeight,
        Func<TValue, long> valueWeight,
        Func<TValue, bool>? shouldRetain = null)
        : this(maximumCount: null, maximumWeight, valueWeight, shouldRetain, factory: null)
    {
    }

    public SingleFlightLruCache(
        int capacity,
        long maximumWeight,
        Func<TValue, long> valueWeight,
        Func<TValue, bool>? shouldRetain = null)
        : this(capacity, maximumWeight, valueWeight, shouldRetain, factory: null)
    {
    }

    private SingleFlightLruCache(
        int? maximumCount,
        long? maximumWeight,
        Func<TValue, long>? valueWeight,
        Func<TValue, bool>? shouldRetain,
        Func<TKey, TValue>? factory)
    {
        if (maximumCount is { } count)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        }

        if (maximumWeight is { } weight)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weight);
        }

        if (maximumWeight is not null && valueWeight is null)
        {
            throw new ArgumentNullException(nameof(valueWeight));
        }

        this.maximumCount = maximumCount;
        this.maximumWeight = maximumWeight;
        this.valueWeight = valueWeight;
        this.shouldRetain = shouldRetain;
        this.factory = factory;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return retained.Count;
            }
        }
    }

    public long RetainedWeight
    {
        get
        {
            lock (gate)
            {
                return retainedWeight;
            }
        }
    }

    public TValue GetOrAdd(TKey key)
    {
        if (factory is null)
        {
            throw new InvalidOperationException("A value factory must be supplied.");
        }

        return GetOrAdd(key, factory);
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        Lazy<TValue> pending;
        lock (gate)
        {
            if (TryGetRetainedLocked(key, out var value))
            {
                return value;
            }

            if (!pendingSync.TryGetValue(key, out pending!))
            {
                pending = new Lazy<TValue>(
                    () => valueFactory(key),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                pendingSync.Add(key, pending);
            }
        }

        try
        {
            var value = pending.Value;
            CompleteSync(key, pending, value);
            return value;
        }
        catch
        {
            DetachSync(key, pending);
            throw;
        }
    }

    public async Task<TValue> GetOrAddAsync(
        TKey key,
        Func<TKey, CancellationToken, Task<TValue>> valueFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        cancellationToken.ThrowIfCancellationRequested();

        Lazy<Task<TValue>> pending;
        lock (gate)
        {
            if (TryGetRetainedLocked(key, out var value))
            {
                return value;
            }

            if (!pendingAsync.TryGetValue(key, out pending!))
            {
                pending = CreateAsyncLazy(key, valueFactory);
                pendingAsync.Add(key, pending);
            }
        }

        Task<TValue> producerTask;
        try
        {
            producerTask = pending.Value;
        }
        catch
        {
            DetachAsync(key, pending);
            throw;
        }

        return cancellationToken.CanBeCanceled
            ? await producerTask.WaitAsync(cancellationToken).ConfigureAwait(false)
            : await producerTask.ConfigureAwait(false);
    }

    public void Remove(TKey key)
    {
        lock (gate)
        {
            RemoveRetainedLocked(key);
            pendingSync.Remove(key);
            pendingAsync.Remove(key);
        }
    }

    public void Remove(TKey key, TValue value)
    {
        lock (gate)
        {
            if (retained.TryGetValue(key, out var node) &&
                EqualityComparer<TValue>.Default.Equals(node.Value.Value, value))
            {
                RemoveRetainedNodeLocked(node);
            }
        }
    }

    public void RemoveWhere(Predicate<TKey> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (gate)
        {
            var node = lru.First;
            while (node is not null)
            {
                var next = node.Next;
                if (predicate(node.Value.Key))
                {
                    RemoveRetainedNodeLocked(node);
                }

                node = next;
            }

            RemovePendingWhereLocked(pendingSync, predicate);
            RemovePendingWhereLocked(pendingAsync, predicate);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            retained.Clear();
            lru.Clear();
            pendingSync.Clear();
            pendingAsync.Clear();
            retainedWeight = 0;
        }
    }

    private bool TryGetRetainedLocked(TKey key, out TValue value)
    {
        if (retained.TryGetValue(key, out var node))
        {
            lru.Remove(node);
            lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    private void CompleteSync(TKey key, Lazy<TValue> pending, TValue value)
    {
        lock (gate)
        {
            if (!pendingSync.TryGetValue(key, out var current) || !ReferenceEquals(current, pending))
            {
                return;
            }

            pendingSync.Remove(key);
            RetainLocked(key, value);
        }
    }

    private Lazy<Task<TValue>> CreateAsyncLazy(
        TKey key,
        Func<TKey, CancellationToken, Task<TValue>> valueFactory)
    {
        Lazy<Task<TValue>>? pending = null;
        pending = new Lazy<Task<TValue>>(
            () => ObserveProducerAsync(key, pending!, valueFactory),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return pending;
    }

    private async Task<TValue> ObserveProducerAsync(
        TKey key,
        Lazy<Task<TValue>> pending,
        Func<TKey, CancellationToken, Task<TValue>> valueFactory)
    {
        try
        {
            var task = valueFactory(key, CancellationToken.None) ??
                       throw new InvalidOperationException("The value factory returned a null task.");
            var value = await task.ConfigureAwait(false);

            lock (gate)
            {
                if (pendingAsync.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
                {
                    pendingAsync.Remove(key);
                    RetainLocked(key, value);
                }
            }

            return value;
        }
        catch
        {
            DetachAsync(key, pending);
            throw;
        }
    }

    private void RetainLocked(TKey key, TValue value)
    {
        if (shouldRetain is not null && !shouldRetain(value))
        {
            return;
        }

        var weight = valueWeight is null ? 0 : valueWeight(value);
        if (weight < 0)
        {
            throw new InvalidOperationException("A cache value weight cannot be negative.");
        }

        if (maximumWeight is { } weightLimit && weight > weightLimit)
        {
            return;
        }

        RemoveRetainedLocked(key);
        var entry = new RetainedEntry(key, value, weight);
        var node = lru.AddFirst(entry);
        retained.Add(key, node);
        retainedWeight += weight;
        EvictOverflowLocked();
    }

    private void EvictOverflowLocked()
    {
        while (maximumCount is { } countLimit && retained.Count > countLimit && lru.Last is { } countTail)
        {
            RemoveRetainedNodeLocked(countTail);
        }

        while (maximumWeight is { } weightLimit && retainedWeight > weightLimit && lru.Last is { } weightTail)
        {
            RemoveRetainedNodeLocked(weightTail);
        }
    }

    private void RemoveRetainedLocked(TKey key)
    {
        if (retained.TryGetValue(key, out var node))
        {
            RemoveRetainedNodeLocked(node);
        }
    }

    private void RemoveRetainedNodeLocked(LinkedListNode<RetainedEntry> node)
    {
        lru.Remove(node);
        retained.Remove(node.Value.Key);
        retainedWeight -= node.Value.Weight;
    }

    private void DetachSync(TKey key, Lazy<TValue> pending)
    {
        lock (gate)
        {
            if (pendingSync.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
            {
                pendingSync.Remove(key);
            }
        }
    }

    private void DetachAsync(TKey key, Lazy<Task<TValue>> pending)
    {
        lock (gate)
        {
            if (pendingAsync.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
            {
                pendingAsync.Remove(key);
            }
        }
    }

    private static void RemovePendingWhereLocked<TPending>(
        Dictionary<TKey, TPending> pending,
        Predicate<TKey> predicate)
    {
        List<TKey>? matchingKeys = null;
        foreach (var key in pending.Keys)
        {
            if (predicate(key))
            {
                (matchingKeys ??= []).Add(key);
            }
        }

        if (matchingKeys is null)
        {
            return;
        }

        foreach (var key in matchingKeys)
        {
            pending.Remove(key);
        }
    }

    private sealed record RetainedEntry(TKey Key, TValue Value, long Weight);
}
