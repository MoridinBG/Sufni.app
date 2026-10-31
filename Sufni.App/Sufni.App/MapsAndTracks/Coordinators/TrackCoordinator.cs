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
        TelemetryData telemetryData,
        CancellationToken cancellationToken = default)
    {
        return backgroundTaskRunner.RunAsync(
            () => LoadSessionTrackCoreAsync(sessionId, telemetryData, cancellationToken),
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
        TelemetryData telemetryData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = await sessionRepository.GetSessionAsync(sessionId);
        if (session is null) return new SessionTrackPresentationData(null, null, null, null);
        var timestamp = session.Timestamp ?? telemetryData.Metadata.Timestamp;
        var duration = session.DurationSeconds ?? telemetryData.Metadata.Duration;
        var gpsOffset = SessionTrackTimeline.NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds);
        var selected = SessionTrackSelection.Select(trackStore.GetAll(), timestamp, duration, gpsOffset);
        if (selected is null) return new SessionTrackPresentationData(null, null, null, null);

        var fullPoints = await fullTrackPointReader.GetTrackPointsExactAsync(selected.Id, selected.PointsRevision, cancellationToken);
        if (fullPoints is null) return new SessionTrackPresentationData(selected.Id, null, null, null);
        var key = new SessionTrackKey(selected.Id, selected.PointsRevision, timestamp, duration, gpsOffset);
        var points = await sessionTrackReader.GetSessionTrackAsync(key, cancellationToken);
        return new SessionTrackPresentationData(selected.Id, AsList(fullPoints), AsNullableList(points), DefaultMediaColumnWidth);
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
