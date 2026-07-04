using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Infrastructure.Caching;

internal sealed class SingleFlightLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int capacity;
    private readonly Func<TKey, TValue>? factory;
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<EntryKey, LinkedListNode<Entry>> entries = new();
    private readonly LinkedList<Entry> lru = new();

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
            return lazy.Value;
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
                    lru.Remove(node);
                    entries.Remove(node.Value.CacheKey);
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
        }
    }

    private void EvictOverflow()
    {
        while (entries.Count > capacity && lru.Last is { } tail)
        {
            lru.RemoveLast();
            entries.Remove(tail.Value.CacheKey);
        }
    }

    private void RemoveLocked(EntryKey entryKey)
    {
        if (entries.Remove(entryKey, out var node))
        {
            lru.Remove(node);
        }
    }

    private void RemoveFailedValue(EntryKey entryKey, Lazy<TValue> lazy)
    {
        lock (gate)
        {
            if (entries.TryGetValue(entryKey, out var node) && ReferenceEquals(node.Value.SyncValue, lazy))
            {
                lru.Remove(node);
                entries.Remove(entryKey);
            }
        }
    }

    private void RemoveFailedAsyncValue(EntryKey entryKey, Lazy<Task<TValue>> lazy)
    {
        lock (gate)
        {
            if (entries.TryGetValue(entryKey, out var node) && ReferenceEquals(node.Value.AsyncValue, lazy))
            {
                lru.Remove(node);
                entries.Remove(entryKey);
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
        _ = task.ContinueWith(
            static (completedTask, state) =>
            {
                if (!completedTask.IsCanceled && !completedTask.IsFaulted)
                {
                    return;
                }

                var failure = (AsyncFailureState)state!;
                failure.Cache.RemoveFailedAsyncValue(failure.EntryKey, failure.Lazy);
            },
            new AsyncFailureState(this, entryKey, lazy),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private enum EntryKind
    {
        Sync,
        Async,
    }

    private readonly record struct EntryKey(EntryKind Kind, TKey Key);

    private sealed record Entry(
        EntryKey CacheKey,
        TKey Key,
        Lazy<TValue>? SyncValue,
        Lazy<Task<TValue>>? AsyncValue)
    {
        public static Entry CreateSync(EntryKey cacheKey, Lazy<TValue> value) =>
            new(cacheKey, cacheKey.Key, value, AsyncValue: null);

        public static Entry CreateAsync(EntryKey cacheKey, Lazy<Task<TValue>> value) =>
            new(cacheKey, cacheKey.Key, SyncValue: null, value);
    }

    private sealed record AsyncFailureState(
        SingleFlightLruCache<TKey, TValue> Cache,
        EntryKey EntryKey,
        Lazy<Task<TValue>> Lazy);
}
