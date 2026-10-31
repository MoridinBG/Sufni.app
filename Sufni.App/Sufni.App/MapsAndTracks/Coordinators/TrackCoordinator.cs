using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Models;
using Serilog;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Sessions.Models;
namespace Sufni.App.MapsAndTracks.Coordinators;

public class TrackCoordinator(
    ITrackRepository trackRepository,
    ISynchronizableRepository<Track> trackEntityRepository,
    ISessionRepository sessionRepository,
    IFilesService filesService,
    IBackgroundTaskRunner backgroundTaskRunner,
    ISessionTrackReader sessionTrackReader,
    IFullTrackPointReader fullTrackPointReader,
    IGpxTrackReader gpxTrackReader,
    ITrackStoreWriter trackStore) : ITrackCoordinator
{
    private static readonly ILogger logger = Log.ForContext<TrackCoordinator>();
    private const double DefaultMediaColumnWidth = 400.0;

    public async Task<GpxImportResult> ImportGpxAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var files = await filesService.OpenGpxFilesAsync();
            if (files.Count == 0) return new GpxImportResult.Canceled();

            return await backgroundTaskRunner.RunAsync(
                () => ImportGpxCoreAsync(files, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.Error(e, "GPX import could not start");
            return new GpxImportResult.Failed(GpxImportFailureReason.Import, e.Message);
        }
    }

    public Task<SessionTrackPresentationData> LoadSessionTrackAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken = default)
    {
        return backgroundTaskRunner.RunAsync(
            () => LoadSessionTrackCoreAsync(sessionId, fullTrackId, telemetryData, cancellationToken),
            cancellationToken);
    }

    private async Task<GpxImportResult> ImportGpxCoreAsync(
        IReadOnlyList<PickedFile> files,
        CancellationToken cancellationToken)
    {
        var importedCount = 0;
        var alreadyImportedCount = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await gpxTrackReader.ReadAsync(file, cancellationToken);
                if (result is GpxTrackReadResult.Failed failure)
                    return new GpxImportResult.Failed(failure.Reason, failure.Details, importedCount, alreadyImportedCount);
                var track = new Track(((GpxTrackReadResult.Read)result).Points);

                var existingTrack = await trackEntityRepository.GetAsync(track.Id);
                if (existingTrack is not null)
                {
                    alreadyImportedCount++;
                    continue;
                }

                await trackEntityRepository.PutAsync(track);
                await trackStore.PublishTracksChangedAsync(await trackRepository.GetTrackSnapshotsAsync([track.Id]));
                importedCount++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.Error(e, "GPX import failed for {FileName}", file.Name);
                return new GpxImportResult.Failed(GpxImportFailureReason.Import, e.Message,
                    importedCount, alreadyImportedCount);
            }
        }

        return new GpxImportResult.Completed(importedCount, alreadyImportedCount);
    }

    private async Task<SessionTrackPresentationData> LoadSessionTrackCoreAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = await sessionRepository.GetSessionAsync(sessionId);
        var gpsOffsetSeconds = SessionTrackTimeline.NormalizeGpsOffsetSeconds(session?.GpsOffsetSeconds ?? 0);

        // Read-only load: the snapshot's full_track_id is the only source of the
        // association. The write/recompute path owns establishing it, so an
        // unassociated session simply renders without a track here.
        var resolvedFullTrackId = fullTrackId;
        if (resolvedFullTrackId is null)
        {
            return new SessionTrackPresentationData(null, null, null, null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullTrackPoints = await fullTrackPointReader.GetTrackPointsAsync(resolvedFullTrackId.Value, cancellationToken);
        if (fullTrackPoints is null)
        {
            return new SessionTrackPresentationData(resolvedFullTrackId, null, null, null);
        }

        var fullTrackPointList = AsList(fullTrackPoints);
        var trackPoints = await sessionTrackReader.GetSessionTrackAsync(
            sessionId,
            session?.TrackProjectionRevision ?? 0,
            cancellationToken);

        // When the cached session-window polyline is missing or no longer aligned
        // with the current GPS offset, regenerate it in memory for display only.
        // Persisting the cached polyline is the processed-write path's job.
        if (!SessionTrackProjection.IsSessionTrackAligned(trackPoints, telemetryData, gpsOffsetSeconds))
        {
            var fullTrack = new Track
            {
                Id = resolvedFullTrackId.Value,
                Points = fullTrackPointList,
            };
            trackPoints = SessionTrackProjection.GenerateSessionTrack(fullTrack, telemetryData, gpsOffsetSeconds);
        }

        return new SessionTrackPresentationData(
            resolvedFullTrackId,
            fullTrackPointList,
            AsNullableList(trackPoints),
            DefaultMediaColumnWidth);
    }

    private static List<TrackPoint> AsList(IReadOnlyList<TrackPoint> points)
    {
        return points switch
        {
            List<TrackPoint> list => list,
            _ => points.ToList(),
        };
    }

    private static List<TrackPoint>? AsNullableList(IReadOnlyList<TrackPoint>? points)
    {
        return points switch
        {
            null => null,
            List<TrackPoint> list => list,
            _ => points.ToList(),
        };
    }
}

public abstract record GpxImportResult
{
    private GpxImportResult() { }
    public sealed record Canceled : GpxImportResult;
    public sealed record Completed(int ImportedCount, int AlreadyImportedCount) : GpxImportResult;
    public sealed record Failed(GpxImportFailureReason Reason, string? Details = null,
        int ImportedCount = 0, int AlreadyImportedCount = 0) : GpxImportResult;
}
