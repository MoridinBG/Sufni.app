using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;

namespace Sufni.App.MapsAndTracks.Services;

public interface ISessionTrackReader
{
    Task<IReadOnlyList<TrackPoint>?> GetSessionTrackAsync(
        Guid sessionId,
        long sessionUpdated,
        CancellationToken cancellationToken = default);
}

internal sealed class SessionTrackReader : ISessionTrackReader, IDisposable
{
    private const int DefaultCapacity = 64;

    private readonly ISessionRepository sessionRepository;
    private readonly TrackPointReaderCache<SessionTrackCacheKey, IReadOnlyList<TrackPoint>?> cache;
    private readonly IDisposable sessionSubscription;
    private bool disposed;

    public SessionTrackReader(ISessionRepository sessionRepository, ISessionStore sessionStore)
        : this(sessionRepository, sessionStore, DefaultCapacity)
    {
    }

    internal SessionTrackReader(
        ISessionRepository sessionRepository,
        ISessionStore sessionStore,
        int capacity)
    {
        this.sessionRepository = sessionRepository;
        cache = new TrackPointReaderCache<SessionTrackCacheKey, IReadOnlyList<TrackPoint>?>(capacity);
        sessionSubscription = sessionStore.Connect().Subscribe(ApplySessionChanges);
    }

    public Task<IReadOnlyList<TrackPoint>?> GetSessionTrackAsync(
        Guid sessionId,
        long sessionUpdated,
        CancellationToken cancellationToken = default)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(SessionTrackReader));
        }

        return cache.GetOrAddAsync(
            new SessionTrackCacheKey(sessionId, sessionUpdated),
            async (key, _) => await sessionRepository.GetSessionTrackAsync(key.SessionId).ConfigureAwait(false),
            cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        sessionSubscription.Dispose();
        cache.Clear();
    }

    private void ApplySessionChanges(IChangeSet<SessionSnapshot, Guid> changes)
    {
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Refresh:
                    cache.RemoveWhere(key => key.SessionId == change.Key);
                    break;
                case ChangeReason.Update:
                    if (!change.Previous.HasValue || ShouldEvict(change.Previous.Value, change.Current))
                    {
                        cache.RemoveWhere(key => key.SessionId == change.Key);
                    }

                    break;
                case ChangeReason.Remove:
                    cache.RemoveWhere(key => key.SessionId == change.Key);
                    break;
                case ChangeReason.Moved:
                    break;
            }
        }
    }

    private static bool ShouldEvict(SessionSnapshot previous, SessionSnapshot current)
    {
        return previous.Updated != current.Updated ||
               previous.FullTrackId != current.FullTrackId ||
               previous.GpsOffsetSeconds != current.GpsOffsetSeconds;
    }

    private readonly record struct SessionTrackCacheKey(Guid SessionId, long SessionUpdated);
}

internal sealed class TrackPointReaderCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int capacity;
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = new();
    private readonly LinkedList<Entry> lru = new();

    public TrackPointReaderCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
    }

    public Task<TValue> GetOrAddAsync(
        TKey key,
        Func<TKey, CancellationToken, Task<TValue>> factory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<TValue> task;
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existingNode))
            {
                lru.Remove(existingNode);
                lru.AddFirst(existingNode);
                task = existingNode.Value.Value;
            }
            else
            {
                task = RunFactoryAsync(key, factory, cancellationToken);
                var node = new LinkedListNode<Entry>(new Entry(key, task));
                lru.AddFirst(node);
                entries.Add(key, node);
                EvictOverflow();
            }
        }

        return RemoveFailedValueAsync(key, task);
    }

    public void Remove(TKey key)
    {
        lock (gate)
        {
            RemoveLocked(key);
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

    private static async Task<TValue> RunFactoryAsync(
        TKey key,
        Func<TKey, CancellationToken, Task<TValue>> factory,
        CancellationToken cancellationToken)
    {
        return await factory(key, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TValue> RemoveFailedValueAsync(TKey key, Task<TValue> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            lock (gate)
            {
                if (entries.TryGetValue(key, out var node) && ReferenceEquals(node.Value.Value, task))
                {
                    lru.Remove(node);
                    entries.Remove(key);
                }
            }

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

    private void RemoveLocked(TKey key)
    {
        if (entries.Remove(key, out var node))
        {
            lru.Remove(node);
        }
    }

    private sealed record Entry(TKey Key, Task<TValue> Value);
}
