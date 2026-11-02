using Sufni.App.Sessions.Store;
using System;
using System.Collections.Immutable;
using System.Reactive.Linq;
using DynamicData;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

}

internal sealed class VideoCoordinator(
    IVideoImportFileService fileService,
    ISessionRepository sessionRepository,
    ISessionVideoRepository videoRepository,
    ISessionVideoStoreWriter videoStore,
    IBackgroundTaskRunner backgroundTaskRunner,
    TimeProvider timeProvider,
    IRecordedSessionSourceStore sourceStore,
    IVideoFilePresence filePresence,
    IUiThreadDispatcher dispatcher) : IVideoCoordinator, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<VideoCoordinator>();
    private readonly IDisposable presenceSubscription = ObservePresence(videoStore, filePresence, dispatcher);

    private static IDisposable ObservePresence(ISessionVideoStoreWriter store, IVideoFilePresence presence, IUiThreadDispatcher dispatcher) =>
        store.Connect().ToCollection()
            .Select(clips => clips.Select(clip => clip.Path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray())
            .DistinctUntilChanged(EqualityComparer<ImmutableArray<string>>.Create((left, right) => left.SequenceEqual(right)))
            .Select(paths => presence.Observe(paths))
            .Switch()
            .Select(observed => Observable.FromAsync(token => dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return Task.CompletedTask;
                var changed = store.GetAll()
                    .Where(clip => observed.TryGetValue(clip.Path, out var exists) && exists != clip.FileExists)
                    .Select(clip => clip with { FileExists = observed[clip.Path] }).ToArray();
                return changed.Length == 0 ? Task.CompletedTask : store.PublishClipsAsync(changed);
            })))
            .Concat()
            .Subscribe(_ => { }, exception => logger.Error(exception, "Video file presence observation failed"));

    public void Dispose() => presenceSubscription.Dispose();

    public async Task<VideoImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await fileService.ReadAsync(cancellationToken);
            return result switch
            {
                VideoFileReadResult.Cancelled => new VideoImportResult.Cancelled(),
                VideoFileReadResult.Failed failed => new VideoImportResult.Failed(failed.Reason, failed.Details),
                VideoFileReadResult.Read read => await backgroundTaskRunner.RunAsync(
                    () => ImportCoreAsync(read, cancellationToken), cancellationToken),
                _ => throw new InvalidOperationException("Unknown video file result")
            };
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
            var updated = await backgroundTaskRunner.RunAsync(() =>
                videoRepository.UpdateAlignmentAsync(clipId, clip.AlignmentOffsetSeconds + deltaSeconds));
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

    private async Task<VideoImportResult> ImportCoreAsync(
        VideoFileReadResult.Read file,
        CancellationToken cancellationToken)
    {
        var metadata = file.Metadata;
        var sessions = await sessionRepository.GetSessionsAsync();
        var matchingSessions = sessions
            .Where(session => GetMatchFailureReason(session, metadata) is null)
            .ToArray();
        if (matchingSessions.Length == 0)
        {
            LogMatchFailures(file.Name, metadata, sessions);
            return new VideoImportResult.NoMatchingSession();
        }

        var inputs = matchingSessions.Select(session => (Session: session, Source: sourceStore.Get(session.SourceSessionId ?? session.Id))).ToArray();
        if (inputs.Any(input => input.Source is null))
            return new VideoImportResult.SourceUnavailable();

        cancellationToken.ThrowIfCancellationRequested();
        var clips = await videoRepository.LinkAsync(
            file.Path,
            metadata.StartUtc.ToUnixTimeSeconds(),
            metadata.Duration.TotalSeconds,
            timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            inputs.Select(input => new SessionVideoOwner(input.Source!.SourceHash,
                SessionTrackTimeline.FromSessionWindow(input.Session.Timestamp!.Value, 0, input.Session.WindowStartSeconds).SourceTimestamp)).ToArray());
        await videoStore.PublishClipsAsync(clips.ToArray());
        return new VideoImportResult.Linked(matchingSessions.Length);
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
