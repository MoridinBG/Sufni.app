using System;
using System.Collections.Generic;
using System.Threading;

namespace Sufni.App.Infrastructure.Caching;

internal sealed class SingleFlightLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int capacity;
    private readonly Func<TKey, TValue> factory;
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = new();
    private readonly LinkedList<Entry> lru = new();

    public SingleFlightLruCache(int capacity, Func<TKey, TValue> factory)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.capacity = capacity;
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public TValue GetOrAdd(TKey key)
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
                    () => factory(key),
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

    private void EvictOverflow()
    {
        while (entries.Count > capacity && lru.Last is { } tail)
        {
            lru.RemoveLast();
            entries.Remove(tail.Value.Key);
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
