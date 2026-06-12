using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.Coordinators;

public class TrackCoordinator(
    ITrackRepository trackRepository,
    ISynchronizableRepository<Track> trackEntityRepository,
    ISessionRepository sessionRepository,
    ISessionTelemetryWriter sessionTelemetryWriter,
    IFilesService filesService,
    IBackgroundTaskRunner backgroundTaskRunner) : ITrackCoordinator
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

    public Task<SessionGpsOffsetUpdateResult?> UpdateSessionGpsOffsetAsync(
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
        var gpsOffsetSeconds = NormalizeGpsOffsetSeconds(session?.GpsOffsetSeconds ?? 0);
        var resolvedFullTrackId = fullTrackId ?? await trackRepository.AssociateSessionWithTrackAsync(sessionId);
        if (resolvedFullTrackId is null)
        {
            return new SessionTrackPresentationData(null, null, null, null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullTrack = (await trackEntityRepository.GetAsync(resolvedFullTrackId.Value))!;
        var trackPoints = await sessionRepository.GetSessionTrackAsync(sessionId);

        if (!IsSessionTrackAligned(trackPoints, telemetryData, gpsOffsetSeconds))
        {
            trackPoints = GenerateSessionTrack(fullTrack, telemetryData, gpsOffsetSeconds);
            await sessionTelemetryWriter.PatchSessionTrackAsync(sessionId, trackPoints);
        }

        return new SessionTrackPresentationData(
            resolvedFullTrackId,
            fullTrack.Points,
            trackPoints,
            DefaultMediaColumnWidth);
    }

    private async Task<SessionGpsOffsetUpdateResult?> UpdateSessionGpsOffsetCoreAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        double gpsOffsetSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedFullTrackId = fullTrackId ?? await trackRepository.AssociateSessionWithTrackAsync(sessionId);
        if (resolvedFullTrackId is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullTrack = await trackEntityRepository.GetAsync(resolvedFullTrackId.Value);
        if (fullTrack is null)
        {
            return null;
        }

        var normalizedOffset = NormalizeGpsOffsetSeconds(gpsOffsetSeconds);
        var trackPoints = GenerateSessionTrack(fullTrack, telemetryData, normalizedOffset);
        if (trackPoints.Count == 0)
        {
            return null;
        }

        await sessionTelemetryWriter.PatchSessionTrackAsync(sessionId, trackPoints, normalizedOffset);
        var updatedSession = await sessionRepository.GetSessionAsync(sessionId);
        if (updatedSession is null)
        {
            return null;
        }

        var trackData = new SessionTrackPresentationData(
            resolvedFullTrackId,
            fullTrack.Points,
            trackPoints,
            DefaultMediaColumnWidth);
        return new SessionGpsOffsetUpdateResult(SessionSnapshot.From(updatedSession), trackData);
    }

    private static List<TrackPoint> GenerateSessionTrack(
        Track fullTrack,
        TelemetryData telemetryData,
        double gpsOffsetSeconds)
    {
        var start = telemetryData.Metadata.Timestamp + gpsOffsetSeconds;
        var end = start + Math.Ceiling(telemetryData.Metadata.Duration);
        return fullTrack.GenerateSessionTrack(start, end);
    }

    private static bool IsSessionTrackAligned(
        IReadOnlyList<TrackPoint>? trackPoints,
        TelemetryData telemetryData,
        double gpsOffsetSeconds)
    {
        if (trackPoints is null || trackPoints.Count == 0)
        {
            return false;
        }

        var expectedStart = telemetryData.Metadata.Timestamp + gpsOffsetSeconds;
        return Math.Abs(trackPoints[0].Time - expectedStart) <= 1e-6;
    }

    private static double NormalizeGpsOffsetSeconds(double gpsOffsetSeconds) =>
        double.IsFinite(gpsOffsetSeconds) ? gpsOffsetSeconds : 0;
}

public sealed record GpxImportResult(int ImportedCount, int AlreadyImportedCount);
