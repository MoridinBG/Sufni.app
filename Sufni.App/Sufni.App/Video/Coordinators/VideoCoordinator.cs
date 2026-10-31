using Sufni.App.Sessions.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Serilog;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Video.Services;
using Sufni.App.Video.Stores;
namespace Sufni.App.Video.Coordinators;

public abstract record VideoImportResult
{
    private VideoImportResult() { }

    public sealed record Cancelled : VideoImportResult;
    public sealed record Linked(int SessionCount) : VideoImportResult;
    public sealed record NoMatchingSession : VideoImportResult;

    public sealed record SourceUnavailable : VideoImportResult;
    public sealed record Failed(VideoImportFailureReason Reason, string? Details = null) : VideoImportResult;
}

public abstract record VideoAlignmentResult
{
    private VideoAlignmentResult() { }

    public sealed record Updated : VideoAlignmentResult;
    public sealed record ClipMissing : VideoAlignmentResult;
    public sealed record Failed(VideoAlignmentFailureReason Reason, string? Details = null) : VideoAlignmentResult;
}

/// <summary>
/// Owns the video workflows: importing a file and linking it to every
/// recorded session whose recording window overlaps the video, and saving
/// alignment corrections.
/// </summary>
public interface IVideoCoordinator
{
    Task<VideoImportResult> ImportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="deltaSeconds"/> to the clip's alignment offset.
    /// </summary>
    Task<VideoAlignmentResult> ShiftAlignmentAsync(Guid clipId, double deltaSeconds);

    Task RefreshAsync();
}

internal sealed class VideoCoordinator(
    IFilesService filesService,
    IVideoMetadataReader metadataReader,
    ISessionRepository sessionRepository,
    ISessionVideoRepository videoRepository,
    ISessionVideoStoreWriter videoStore,
    IBackgroundTaskRunner backgroundTaskRunner,
    TimeProvider timeProvider,
    IRecordedSessionSourceStore sourceStore) : IVideoCoordinator
{
    private static readonly ILogger logger = Log.ForContext<VideoCoordinator>();

    public async Task<VideoImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await filesService.OpenVideoFileAsync();
            if (file is null)
            {
                return new VideoImportResult.Cancelled();
            }

            var localPath = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
            {
                return new VideoImportResult.Failed(VideoImportFailureReason.LocalFileRequired);
            }

            return await backgroundTaskRunner.RunAsync(
                () => ImportCoreAsync(file, Path.GetFullPath(localPath), cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new VideoImportResult.Cancelled();
        }
        catch (Exception e)
        {
            logger.Error(e, "Video import failed");
            return new VideoImportResult.Failed(VideoImportFailureReason.Import, e.Message);
        }
    }

    public async Task<VideoAlignmentResult> ShiftAlignmentAsync(Guid clipId, double deltaSeconds)
    {
        if (videoStore.Get(clipId) is not { } clip)
        {
            return new VideoAlignmentResult.ClipMissing();
        }

        try
        {
            var updated = await backgroundTaskRunner.RunAsync(async () =>
            {
                var row = await videoRepository.UpdateAlignmentAsync(clipId, clip.AlignmentOffsetSeconds + deltaSeconds);
                return row is null ? null : SessionVideoSnapshot.From(row);
            });
            if (updated is null)
            {
                return new VideoAlignmentResult.ClipMissing();
            }

            await videoStore.PublishClipsAsync([updated]);
            return new VideoAlignmentResult.Updated();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.Error(e, "Video alignment failed for {ClipId}", clipId);
            return new VideoAlignmentResult.Failed(VideoAlignmentFailureReason.Save, e.Message);
        }
    }

    public async Task RefreshAsync()
    {
        var snapshots = await backgroundTaskRunner.RunAsync(async () =>
            (await videoRepository.GetLiveAsync()).Select(SessionVideoSnapshot.From).ToArray());
        await videoStore.ReplaceAllAsync(snapshots);
    }

    private async Task<VideoImportResult> ImportCoreAsync(
        IStorageFile file,
        string path,
        CancellationToken cancellationToken)
    {
        VideoMetadataReadResult metadataResult;
        await using (var stream = await file.OpenReadAsync())
        {
            metadataResult = await metadataReader.ReadAsync(stream, cancellationToken);
        }

        if (metadataResult is VideoMetadataReadResult.Invalid invalid)
        {
            return new VideoImportResult.Failed(VideoImportFailureReason.InvalidMetadata, invalid.ErrorMessage);
        }

        var metadata = ((VideoMetadataReadResult.Parsed)metadataResult).Metadata;
        var sessions = await sessionRepository.GetSessionsAsync();
        var matchingSessions = sessions
            .Where(session => GetMatchFailureReason(session, metadata) is null)
            .ToArray();
        if (matchingSessions.Length == 0)
        {
            LogMatchFailures(file.Name, metadata, sessions);
            return new VideoImportResult.NoMatchingSession();
        }

        if (matchingSessions.Any(session => sourceStore.Get(session.SourceSessionId ?? session.Id) is null))
            return new VideoImportResult.SourceUnavailable();

        cancellationToken.ThrowIfCancellationRequested();
        var clips = await videoRepository.LinkAsync(
            path,
            metadata.StartUtc.ToUnixTimeSeconds(),
            metadata.Duration.TotalSeconds,
            timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            matchingSessions.Select(session => session.Id).ToArray());
        await videoStore.PublishClipsAsync(clips.Select(SessionVideoSnapshot.From).ToArray());
        return new VideoImportResult.Linked(clips.Count);
    }

    // A session matches when any part of the video recording window overlaps
    // the session recording window. Returns null on a match, otherwise the
    // reason logged when an import links no sessions.
    private static string? GetMatchFailureReason(Session session, VideoMetadata metadata)
    {
        if (session.Timestamp is null)
        {
            return "session has no start timestamp";
        }

        if (session.DurationSeconds is not > 0)
        {
            return "session has no positive duration";
        }

        var sessionStartUtc = DateTimeOffset.FromUnixTimeSeconds(session.Timestamp.Value);
        var sessionEndUtc = sessionStartUtc.AddSeconds(session.DurationSeconds.Value);
        if (sessionEndUtc <= metadata.StartUtc)
        {
            var gapSeconds = (metadata.StartUtc - sessionEndUtc).TotalSeconds;
            return $"session ends at {sessionEndUtc:u}, {gapSeconds:0.###} s before the video begins";
        }

        if (sessionStartUtc >= metadata.EndUtc)
        {
            var gapSeconds = (sessionStartUtc - metadata.EndUtc).TotalSeconds;
            return $"session starts at {sessionStartUtc:u} (unix {session.Timestamp.Value}), {gapSeconds:0.###} s after the video ends";
        }

        return null;
    }

    private static void LogMatchFailures(
        string fileName,
        VideoMetadata metadata,
        IReadOnlyList<Session> sessions)
    {
        logger.Debug(
            "Video {FileName} matched no sessions; it covers {VideoStartUtc:u} to {VideoEndUtc:u} (unix {VideoStartUnix}, duration {DurationSeconds:0.###} s)",
            fileName,
            metadata.StartUtc,
            metadata.EndUtc,
            metadata.StartUtc.ToUnixTimeSeconds(),
            metadata.Duration.TotalSeconds);

        foreach (var session in sessions)
        {
            logger.Debug(
                "Session {SessionName} ({SessionId}) did not match video {FileName}: {Reason}",
                session.Name,
                session.Id,
                fileName,
                GetMatchFailureReason(session, metadata));
        }
    }
}
