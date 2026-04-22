using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App.Coordinators;

public sealed class TrackCoordinator(
    IDatabaseService databaseService,
    IFilesService filesService,
    IBackgroundTaskRunner backgroundTaskRunner) : ITrackCoordinator
{
    private const double DefaultMapVideoWidth = 400.0;

    public async Task ImportGpxAsync(CancellationToken cancellationToken = default)
    {
        var files = await filesService.OpenGpxFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        await backgroundTaskRunner.RunAsync(
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

    private async Task ImportGpxCoreAsync(IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files, CancellationToken cancellationToken)
    {
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

            await databaseService.PutAsync(track);
        }
    }

    private async Task<SessionTrackPresentationData> LoadSessionTrackCoreAsync(
        Guid sessionId,
        Guid? fullTrackId,
        TelemetryData telemetryData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedFullTrackId = fullTrackId ?? await databaseService.AssociateSessionWithTrackAsync(sessionId);
        if (resolvedFullTrackId is null)
        {
            return new SessionTrackPresentationData(null, null, null, null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullTrack = await databaseService.GetAsync<Track>(resolvedFullTrackId.Value);
        var trackPoints = await databaseService.GetSessionTrackAsync(sessionId);

        if (trackPoints is null)
        {
            var start = telemetryData.Metadata.Timestamp;
            var end = start + (int)Math.Ceiling(telemetryData.Metadata.Duration);
            trackPoints = fullTrack.GenerateSessionTrack(start, end);
            await databaseService.PatchSessionTrackAsync(sessionId, trackPoints);
        }

        return new SessionTrackPresentationData(
            resolvedFullTrackId,
            fullTrack.Points,
            trackPoints,
            DefaultMapVideoWidth);
    }
}