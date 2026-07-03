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

    private readonly ITrackRepository trackRepository;
    private readonly SingleFlightLruCache<FullTrackPointCacheKey, Task<IReadOnlyList<TrackPoint>?>> cache;

    public FullTrackPointReader(ITrackRepository trackRepository)
        : this(trackRepository, DefaultCapacity)
    {
    }

    internal FullTrackPointReader(ITrackRepository trackRepository, int capacity)
    {
        this.trackRepository = trackRepository;
        cache = new SingleFlightLruCache<FullTrackPointCacheKey, Task<IReadOnlyList<TrackPoint>?>>(capacity);
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
            var task = cache.GetOrAdd(key, LoadTrackPointsAsync);
            var points = await AwaitCachedValueAsync(key, task, cancellationToken);
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
        FullTrackPointCacheKey key)
    {
        var payload = await trackRepository.GetTrackPayloadAsync(key.TrackId, key.Updated);
        return payload?.Points;
    }

    private async Task<IReadOnlyList<TrackPoint>?> AwaitCachedValueAsync(
        FullTrackPointCacheKey key,
        Task<IReadOnlyList<TrackPoint>?> task,
        CancellationToken cancellationToken)
    {
        try
        {
            return cancellationToken.CanBeCanceled
                ? await task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !task.IsCompleted)
        {
            throw;
        }
        catch
        {
            cache.Remove(key, task);
            throw;
        }
    }

    private readonly record struct FullTrackPointCacheKey(Guid TrackId, long Updated);
}
