using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Infrastructure.Caching;

internal sealed class SingleFlightLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int capacity;
    private readonly long? maximumWeight;
    private readonly Func<TValue, long>? valueWeight;
    private readonly Func<TKey, TValue>? factory;
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<EntryKey, LinkedListNode<Entry>> entries = new();
    private readonly LinkedList<Entry> lru = new();
    private long retainedWeight;

    public SingleFlightLruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.capacity = capacity;
    }

    public SingleFlightLruCache(int capacity, Func<TKey, TValue> factory)
        : this(capacity)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public SingleFlightLruCache(int capacity, long maximumWeight, Func<TValue, long> valueWeight)
        : this(capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWeight);

        this.maximumWeight = maximumWeight;
        this.valueWeight = valueWeight ?? throw new ArgumentNullException(nameof(valueWeight));
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

        Lazy<TValue> lazy;
        var entryKey = new EntryKey(EntryKind.Sync, key);

        lock (gate)
        {
            if (entries.TryGetValue(entryKey, out var existingNode))
            {
                lru.Remove(existingNode);
                lru.AddFirst(existingNode);
                lazy = existingNode.Value.SyncValue!;
            }
            else
            {
                lazy = new Lazy<TValue>(
                    () => valueFactory(key),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                var node = new LinkedListNode<Entry>(Entry.CreateSync(entryKey, lazy));

                lru.AddFirst(node);
                entries.Add(entryKey, node);
                EvictOverflow();
            }
        }

        try
        {
            var value = lazy.Value;
            CompleteValue(entryKey, lazy, value);
            return value;
        }
        catch
        {
            RemoveFailedValue(entryKey, lazy);
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

        Lazy<Task<TValue>> lazy;
        var entryKey = new EntryKey(EntryKind.Async, key);

        lock (gate)
        {
            if (entries.TryGetValue(entryKey, out var existingNode))
            {
                lru.Remove(existingNode);
                lru.AddFirst(existingNode);
                lazy = existingNode.Value.AsyncValue!;
            }
            else
            {
                lazy = CreateAsyncLazy(entryKey, valueFactory);
                var node = new LinkedListNode<Entry>(Entry.CreateAsync(entryKey, lazy));

                lru.AddFirst(node);
                entries.Add(entryKey, node);
                EvictOverflow();
            }
        }

        Task<TValue> task;
        try
        {
            task = lazy.Value;
        }
        catch
        {
            RemoveFailedAsyncValue(entryKey, lazy);
            throw;
        }

        try
        {
            return cancellationToken.CanBeCanceled
                ? await task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !task.IsCanceled)
        {
            throw;
        }
        catch
        {
            if (task.IsCanceled || task.IsFaulted)
            {
                RemoveFailedAsyncValue(entryKey, lazy);
            }

            throw;
        }
    }

    public void Remove(TKey key)
    {
        lock (gate)
        {
            RemoveLocked(new EntryKey(EntryKind.Sync, key));
            RemoveLocked(new EntryKey(EntryKind.Async, key));
        }
    }

    public void Remove(TKey key, TValue value)
    {
        var entryKey = new EntryKey(EntryKind.Sync, key);

        lock (gate)
        {
            if (entries.TryGetValue(entryKey, out var node) &&
                node.Value.SyncValue!.IsValueCreated &&
                EqualityComparer<TValue>.Default.Equals(node.Value.SyncValue.Value, value))
            {
                RemoveLocked(entryKey);
            }
        }
    }

    public void RemoveWhere(Predicate<TKey> predicate)
    {
        lock (gate)
        {
            var node = lru.First;
            while (node is not null)
            {
                var next = node.Next;
                if (predicate(node.Value.Key))
                {
                    RemoveNodeLocked(node);
                }

                node = next;
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            lru.Clear();
            retainedWeight = 0;
        }
    }

    private void EvictOverflow()
    {
        while (entries.Count > capacity && lru.Last is { } tail)
        {
            RemoveNodeLocked(tail);
        }

        while (maximumWeight is { } weightLimit && retainedWeight > weightLimit)
        {
            var weightedTail = lru.Last;
            while (weightedTail is not null && !weightedTail.Value.WeightApplied)
            {
                weightedTail = weightedTail.Previous;
            }

            if (weightedTail is null)
            {
                break;
            }

            RemoveNodeLocked(weightedTail);
        }
    }

    private void RemoveLocked(EntryKey entryKey)
    {
        if (entries.TryGetValue(entryKey, out var node))
        {
            RemoveNodeLocked(node);
        }
    }

    private void RemoveNodeLocked(LinkedListNode<Entry> node)
    {
        lru.Remove(node);
        entries.Remove(node.Value.CacheKey);
        if (node.Value.WeightApplied)
        {
            retainedWeight -= node.Value.Weight;
        }
    }

    private void CompleteValue(EntryKey entryKey, Lazy<TValue> lazy, TValue value)
    {
        if (valueWeight is null)
        {
            return;
        }

        lock (gate)
        {
            if (!entries.TryGetValue(entryKey, out var node) || !ReferenceEquals(node.Value.SyncValue, lazy))
            {
                return;
            }

            try
            {
                ApplyWeightLocked(node, GetValueWeight(value));
            }
            catch
            {
                RemoveNodeLocked(node);
                throw;
            }
        }
    }

    private void CompleteAsyncValue(EntryKey entryKey, Lazy<Task<TValue>> lazy, TValue value)
    {
        if (valueWeight is null)
        {
            return;
        }

        lock (gate)
        {
            if (!entries.TryGetValue(entryKey, out var node) || !ReferenceEquals(node.Value.AsyncValue, lazy))
            {
                return;
            }

            try
            {
                ApplyWeightLocked(node, GetValueWeight(value));
            }
            catch
            {
                RemoveNodeLocked(node);
                throw;
            }
        }
    }

    private long GetValueWeight(TValue value)
    {
        var weight = valueWeight!(value);
        if (weight < 0)
        {
            throw new InvalidOperationException("A cache value weight cannot be negative.");
        }

        return weight;
    }

    private void ApplyWeightLocked(LinkedListNode<Entry> node, long weight)
    {
        if (node.Value.WeightApplied)
        {
            return;
        }

        if (maximumWeight is { } weightLimit && weight > weightLimit)
        {
            RemoveNodeLocked(node);
            return;
        }

        node.Value.Weight = weight;
        node.Value.WeightApplied = true;
        retainedWeight += weight;
        EvictOverflow();
    }

    private void RemoveFailedValue(EntryKey entryKey, Lazy<TValue> lazy)
    {
        lock (gate)
        {
            if (entries.TryGetValue(entryKey, out var node) && ReferenceEquals(node.Value.SyncValue, lazy))
            {
                RemoveNodeLocked(node);
            }
        }
    }

    private void RemoveFailedAsyncValue(EntryKey entryKey, Lazy<Task<TValue>> lazy)
    {
        lock (gate)
        {
            if (entries.TryGetValue(entryKey, out var node) && ReferenceEquals(node.Value.AsyncValue, lazy))
            {
                RemoveNodeLocked(node);
            }
        }
    }

    private Lazy<Task<TValue>> CreateAsyncLazy(
        EntryKey entryKey,
        Func<TKey, CancellationToken, Task<TValue>> valueFactory)
    {
        Lazy<Task<TValue>>? lazy = null;
        lazy = new Lazy<Task<TValue>>(
            () => StartAsyncValue(entryKey, lazy!, valueFactory),
            LazyThreadSafetyMode.ExecutionAndPublication);
        return lazy;
    }

    private Task<TValue> StartAsyncValue(
        EntryKey entryKey,
        Lazy<Task<TValue>> lazy,
        Func<TKey, CancellationToken, Task<TValue>> valueFactory)
    {
        var task = valueFactory(entryKey.Key, CancellationToken.None) ??
                   throw new InvalidOperationException("The value factory returned a null task.");
        return ObserveAsyncValue(entryKey, lazy, task);
    }

    private async Task<TValue> ObserveAsyncValue(
        EntryKey entryKey,
        Lazy<Task<TValue>> lazy,
        Task<TValue> task)
    {
        try
        {
            var value = await task.ConfigureAwait(false);
            CompleteAsyncValue(entryKey, lazy, value);
            return value;
        }
        catch
        {
            RemoveFailedAsyncValue(entryKey, lazy);
            throw;
        }
    }

    private enum EntryKind
    {
        Sync,
        Async,
    }

    private readonly record struct EntryKey(EntryKind Kind, TKey Key);

    private sealed class Entry(
        EntryKey cacheKey,
        TKey key,
        Lazy<TValue>? syncValue,
        Lazy<Task<TValue>>? asyncValue)
    {
        public EntryKey CacheKey { get; } = cacheKey;
        public TKey Key { get; } = key;
        public Lazy<TValue>? SyncValue { get; } = syncValue;
        public Lazy<Task<TValue>>? AsyncValue { get; } = asyncValue;
        public long Weight { get; set; }
        public bool WeightApplied { get; set; }

        public static Entry CreateSync(EntryKey cacheKey, Lazy<TValue> value) =>
            new(cacheKey, cacheKey.Key, value, asyncValue: null);

        public static Entry CreateAsync(EntryKey cacheKey, Lazy<Task<TValue>> value) =>
            new(cacheKey, cacheKey.Key, syncValue: null, asyncValue: value);
    }
}
