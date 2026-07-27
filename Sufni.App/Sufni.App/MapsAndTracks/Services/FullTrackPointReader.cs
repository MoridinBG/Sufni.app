using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.Telemetry.Caching;

namespace Sufni.App.MapsAndTracks.Services;

public interface IFullTrackPointReader
{
    Task<IReadOnlyList<TrackPoint>?> GetTrackPointsAsync(
        Guid trackId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackPoint>?> GetTrackPointsExactAsync(
        Guid trackId,
        long pointsRevision,
        CancellationToken cancellationToken = default);
}

internal sealed class FullTrackPointReader : IFullTrackPointReader
{
    private const int DefaultCapacity = 64;
    private const long DefaultPointBudget = 145_000;

    private readonly ITrackRepository trackRepository;
    private readonly SingleFlightLruCache<FullTrackPointCacheKey, IReadOnlyList<TrackPoint>?> cache;

    public FullTrackPointReader(ITrackRepository trackRepository)
        : this(trackRepository, DefaultCapacity, DefaultPointBudget)
    {
    }

    internal FullTrackPointReader(
        ITrackRepository trackRepository,
        int capacity,
        long pointBudget = DefaultPointBudget)
    {
        this.trackRepository = trackRepository;
        cache = new SingleFlightLruCache<FullTrackPointCacheKey, IReadOnlyList<TrackPoint>?>(
            capacity,
            pointBudget,
            static points => points?.Count ?? 0,
            static points => points is not null);
    }

    public async Task<IReadOnlyList<TrackPoint>?> GetTrackPointsAsync(
        Guid trackId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = await trackRepository.GetTrackPayloadMetadataAsync(trackId);
            if (metadata is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var key = new FullTrackPointCacheKey(metadata.Id, metadata.PointsRevision);
            var points = await cache.GetOrAddAsync(key, LoadTrackPointsAsync, cancellationToken);
            if (points is not null || attempt == 1)
            {
                return points;
            }

            // The row changed between the metadata read and the constrained
            // payload read. Drop that stale key and retry once with fresh metadata.
            cache.Remove(key);
        }

        return null;
    }

    public Task<IReadOnlyList<TrackPoint>?> GetTrackPointsExactAsync(
        Guid trackId,
        long pointsRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new FullTrackPointCacheKey(trackId, pointsRevision);
        return cache.GetOrAddAsync(key, LoadTrackPointsAsync, cancellationToken);
    }

    private async Task<IReadOnlyList<TrackPoint>?> LoadTrackPointsAsync(
        FullTrackPointCacheKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = await trackRepository.GetTrackPayloadAsync(key.TrackId, key.PointsRevision);
        return payload?.Points;
    }

    private readonly record struct FullTrackPointCacheKey(Guid TrackId, long PointsRevision);
}
