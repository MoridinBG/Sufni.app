using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Sessions.Models;
namespace Sufni.App.MapsAndTracks.Coordinators;

public class TrackCoordinator(
    ITrackRepository trackRepository,
    ISynchronizableRepository<Track> trackEntityRepository,
    ISessionRepository sessionRepository,
    ISessionStoreWriter sessionStore,
    IFilesService filesService,
    IBackgroundTaskRunner backgroundTaskRunner,
    ISessionTrackReader sessionTrackReader,
    IFullTrackPointReader fullTrackPointReader) : ITrackCoordinator
{
    private const double DefaultMediaColumnWidth = 400.0;

    public async Task<GpxImportResult> ImportGpxAsync(CancellationToken cancellationToken = default)
    {
        var files = await filesService.OpenGpxFilesAsync();
        if (files.Count == 0)
        {
            return new GpxImportResult(0, 0);
        }

        return await backgroundTaskRunner.RunAsync(
            () => ImportGpxCoreAsync(files, cancellationToken),
            cancellationToken);
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

    public Task<bool> UpdateSessionGpsOffsetAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        double gpsOffsetSeconds,
        CancellationToken cancellationToken = default)
    {
        return backgroundTaskRunner.RunAsync(
            () => UpdateSessionGpsOffsetCoreAsync(sessionId, fullTrackId, telemetryData, gpsOffsetSeconds, cancellationToken),
            cancellationToken);
    }

    private async Task<GpxImportResult> ImportGpxCoreAsync(
        IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files,
        CancellationToken cancellationToken)
    {
        var importedCount = 0;
        var alreadyImportedCount = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var gpx = await reader.ReadToEndAsync(cancellationToken);
            var track = Track.FromGpx(gpx);
            if (track is null)
            {
                throw new InvalidOperationException("GPX file did not contain any valid track points.");
            }

            var existingTrackId = await trackRepository.FindTrackByTimeRangeAsync(track.StartTime, track.EndTime);
            if (existingTrackId is not null)
            {
                alreadyImportedCount++;
                continue;
            }

            await trackEntityRepository.PutAsync(track);
            importedCount++;
        }

        return new GpxImportResult(importedCount, alreadyImportedCount);
    }

    private async Task<SessionTrackPresentationData> LoadSessionTrackCoreAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = await sessionRepository.GetSessionAsync(sessionId);
        var gpsOffsetSeconds = SessionTrackProjection.NormalizeGpsOffsetSeconds(session?.GpsOffsetSeconds ?? 0);

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

    private async Task<bool> UpdateSessionGpsOffsetCoreAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        double gpsOffsetSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Association is established by the write/recompute path; a session without
        // a linked full track has nothing to offset.
        var resolvedFullTrackId = fullTrackId;
        if (resolvedFullTrackId is null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullTrack = await trackEntityRepository.GetAsync(resolvedFullTrackId.Value);
        if (fullTrack is null)
        {
            return false;
        }

        var normalizedOffset = SessionTrackProjection.NormalizeGpsOffsetSeconds(gpsOffsetSeconds);
        var trackPoints = SessionTrackProjection.GenerateSessionTrack(fullTrack, telemetryData, normalizedOffset);
        if (trackPoints.Count == 0)
        {
            return false;
        }

        // One-way persist: CommitTrackPatchAsync writes the offset and cached
        // polyline without an optimistic-concurrency guard and without touching the
        // processed BLOB or its fingerprint, so it cannot false-conflict. Publishing
        // the refreshed snapshot lets the session-detail watch reaction refresh the
        // track and baseline like a recompute — no result is pushed to the editor.
        var result = await sessionStore.CommitTrackPatchAsync(
            sessionId,
            trackPoints,
            normalizedOffset,
            cancellationToken);
        return result is StoreMutationResult<SessionSnapshot>.Saved;
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

public sealed record GpxImportResult(int ImportedCount, int AlreadyImportedCount);
