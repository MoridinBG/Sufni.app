using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry.Caching;

namespace Sufni.App.MapsAndTracks.Services;

public interface ISessionTrackReader
{
    Task<IReadOnlyList<TrackPoint>?> GetSessionTrackAsync(
        SessionTrackKey key, CancellationToken cancellationToken = default);
}

internal sealed class SessionTrackReader : ISessionTrackReader, IDisposable
{
    private const int DefaultCapacity = 64;
    private const long DefaultPointBudget = 145_000;
    private readonly IFullTrackPointReader fullTrackPointReader;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly SingleFlightLruCache<SessionTrackKey, IReadOnlyList<TrackPoint>?> cache;
    private bool disposed;

    public SessionTrackReader(IFullTrackPointReader fullTrackPointReader, IBackgroundTaskRunner backgroundTaskRunner)
        : this(fullTrackPointReader, backgroundTaskRunner, DefaultCapacity, DefaultPointBudget) { }

    internal SessionTrackReader(IFullTrackPointReader fullTrackPointReader, IBackgroundTaskRunner backgroundTaskRunner,
        int capacity, long pointBudget = DefaultPointBudget)
    {
        this.fullTrackPointReader = fullTrackPointReader;
        this.backgroundTaskRunner = backgroundTaskRunner;
        cache = new SingleFlightLruCache<SessionTrackKey, IReadOnlyList<TrackPoint>?>(capacity, pointBudget,
            static points => points?.Count ?? 0, static points => points is not null);
    }

    public Task<IReadOnlyList<TrackPoint>?> GetSessionTrackAsync(SessionTrackKey key,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return cache.GetOrAddAsync(key, LoadAsync, cancellationToken);
    }

    private Task<IReadOnlyList<TrackPoint>?> LoadAsync(SessionTrackKey key, CancellationToken cancellationToken) =>
        backgroundTaskRunner.RunAsync<IReadOnlyList<TrackPoint>?>(async () =>
        {
            var points = await fullTrackPointReader.GetTrackPointsExactAsync(key.TrackId, key.PointsRevision, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (points is null) return null;
            var track = new Track { Id = key.TrackId, Points = points.ToList() };
            return SessionTrackProjection.GenerateSessionTrack(track, key.Timestamp,
                key.DurationSeconds, key.GpsOffsetSeconds)?.AsReadOnly() ?? (IReadOnlyList<TrackPoint>)Array.Empty<TrackPoint>();
        }, cancellationToken);

    public void Dispose()
    {
        disposed = true;
        cache.Clear();
    }
}
