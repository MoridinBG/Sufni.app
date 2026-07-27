using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry.Caching;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;

namespace Sufni.App.MapsAndTracks.Services;

public interface ISessionTrackReader
{
    Task<IReadOnlyList<TrackPoint>?> GetSessionTrackAsync(
        Guid sessionId,
        long trackProjectionRevision,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackPoint>?> GetSessionTrackExactAsync(
        Guid sessionId,
        long trackProjectionRevision,
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
            static points => points?.Count ?? 0,
            static points => points is not null);
        sessionSubscription = sessionStore.Connect().Subscribe(ApplySessionChanges);
    }

    public Task<IReadOnlyList<TrackPoint>?> GetSessionTrackAsync(
        Guid sessionId,
        long trackProjectionRevision,
        CancellationToken cancellationToken = default)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(SessionTrackReader));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return GetSessionTrackCoreAsync(sessionId, trackProjectionRevision, cancellationToken);
    }

    public Task<IReadOnlyList<TrackPoint>?> GetSessionTrackExactAsync(
        Guid sessionId,
        long trackProjectionRevision,
        CancellationToken cancellationToken = default)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(SessionTrackReader));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var key = new SessionTrackCacheKey(sessionId, trackProjectionRevision);
        return cache.GetOrAddAsync(key, LoadTrackPointsAsync, cancellationToken);
    }

    private async Task<IReadOnlyList<TrackPoint>?> GetSessionTrackCoreAsync(
        Guid sessionId,
        long trackProjectionRevision,
        CancellationToken cancellationToken)
    {
        var revision = trackProjectionRevision;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = new SessionTrackCacheKey(sessionId, revision);
            var points = await cache.GetOrAddAsync(key, LoadTrackPointsAsync, cancellationToken);
            if (points is not null || attempt == 1)
            {
                return points;
            }

            cache.Remove(key);
            var current = await sessionRepository.GetSessionAsync(sessionId).ConfigureAwait(false);
            if (current is null || current.TrackProjectionRevision == revision)
            {
                return null;
            }

            revision = current.TrackProjectionRevision;
        }

        return null;
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
        return previous.TrackProjectionRevision != current.TrackProjectionRevision;
    }

    private async Task<IReadOnlyList<TrackPoint>?> LoadTrackPointsAsync(
        SessionTrackCacheKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await sessionRepository.GetSessionTrackAsync(key.SessionId, key.TrackProjectionRevision).ConfigureAwait(false);
    }

    private readonly record struct SessionTrackCacheKey(Guid SessionId, long TrackProjectionRevision);
}
