using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Infrastructure.Caching;
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
    private const long DefaultPointBudget = 145_000;

    private readonly ISessionRepository sessionRepository;
    private readonly SingleFlightLruCache<SessionTrackCacheKey, IReadOnlyList<TrackPoint>?> cache;
    private readonly IDisposable sessionSubscription;
    private bool disposed;

    public SessionTrackReader(ISessionRepository sessionRepository, ISessionStore sessionStore)
        : this(sessionRepository, sessionStore, DefaultCapacity, DefaultPointBudget)
    {
    }

    internal SessionTrackReader(
        ISessionRepository sessionRepository,
        ISessionStore sessionStore,
        int capacity,
        long pointBudget = DefaultPointBudget)
    {
        this.sessionRepository = sessionRepository;
        cache = new SingleFlightLruCache<SessionTrackCacheKey, IReadOnlyList<TrackPoint>?>(
            capacity,
            pointBudget,
            static points => points?.Count ?? 0);
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

        cancellationToken.ThrowIfCancellationRequested();

        var key = new SessionTrackCacheKey(sessionId, sessionUpdated);
        return cache.GetOrAddAsync(key, LoadTrackPointsAsync, cancellationToken);
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

    private async Task<IReadOnlyList<TrackPoint>?> LoadTrackPointsAsync(
        SessionTrackCacheKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await sessionRepository.GetSessionTrackAsync(key.SessionId).ConfigureAwait(false);
    }

    private readonly record struct SessionTrackCacheKey(Guid SessionId, long SessionUpdated);
}
