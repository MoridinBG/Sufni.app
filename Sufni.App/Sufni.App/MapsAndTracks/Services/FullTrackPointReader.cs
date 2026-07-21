using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Infrastructure.Caching;

namespace Sufni.App.MapsAndTracks.Services;

public interface IFullTrackPointReader
{
    Task<IReadOnlyList<TrackPoint>?> GetTrackPointsAsync(
        Guid trackId,
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
            static points => points?.Count ?? 0);
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

            var key = new FullTrackPointCacheKey(metadata.Id, metadata.Updated);
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

    private async Task<IReadOnlyList<TrackPoint>?> LoadTrackPointsAsync(
        FullTrackPointCacheKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = await trackRepository.GetTrackPayloadAsync(key.TrackId, key.Updated);
        return payload?.Points;
    }

    private readonly record struct FullTrackPointCacheKey(Guid TrackId, long Updated);
}
