using System;
using System.Collections.Generic;
using System.Threading;

namespace Sufni.App.Infrastructure.Caching;

internal sealed class SingleFlightLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int capacity;
    private readonly Func<TKey, TValue>? factory;
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = new();
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
        Lazy<TValue> lazy;

        lock (gate)
        {
            if (entries.TryGetValue(key, out var existingNode))
            {
                lru.Remove(existingNode);
                lru.AddFirst(existingNode);
                lazy = existingNode.Value.Value;
            }
            else
            {
                lazy = new Lazy<TValue>(
                    () => valueFactory(key),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                var node = new LinkedListNode<Entry>(new Entry(key, lazy));

                lru.AddFirst(node);
                entries.Add(key, node);
                EvictOverflow();
            }
        }

        try
        {
            return lazy.Value;
        }
        catch
        {
            RemoveFailedValue(key, lazy);
            throw;
        }
    }

    public void Remove(TKey key)
    {
        lock (gate)
        {
            RemoveLocked(key);
        }
    }

    public void Remove(TKey key, TValue value)
    {
        lock (gate)
        {
            if (entries.TryGetValue(key, out var node) &&
                node.Value.Value.IsValueCreated &&
                EqualityComparer<TValue>.Default.Equals(node.Value.Value.Value, value))
            {
                RemoveLocked(key);
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
                    entries.Remove(node.Value.Key);
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
            entries.Remove(tail.Value.Key);
        }
    }

    private void RemoveLocked(TKey key)
    {
        if (entries.Remove(key, out var node))
        {
            lru.Remove(node);
        }
    }

    private void RemoveFailedValue(TKey key, Lazy<TValue> lazy)
    {
        lock (gate)
        {
            if (entries.TryGetValue(key, out var node) && ReferenceEquals(node.Value.Value, lazy))
            {
                lru.Remove(node);
                entries.Remove(key);
            }
        }
    }

    private sealed record Entry(TKey Key, Lazy<TValue> Value);
}
