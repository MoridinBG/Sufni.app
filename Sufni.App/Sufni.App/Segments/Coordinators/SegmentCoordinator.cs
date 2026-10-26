using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Services.Matching;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.Telemetry;
namespace Sufni.App.Segments.Coordinators;

public abstract record SegmentChangeResult
{
    private SegmentChangeResult() { }

    public sealed record Applied : SegmentChangeResult;
    public sealed record Failed(string ErrorMessage) : SegmentChangeResult;
}

/// <summary>
/// The selected range of a session to match, with its track timeline and the
/// session's telemetry duration.
/// </summary>
public sealed record SegmentMatchSource(
    Guid SessionId,
    TelemetryTimeRange Range,
    TrackTimeRange TrackTimeline,
    double DurationSeconds);

public abstract record SegmentMatchResult
{
    private SegmentMatchResult() { }

    /// <param name="Matches">Every usable match, ordered by the local time of the matched segment.</param>
    /// <param name="FirstPage">The comparisons of the first matches.</param>
    public sealed record Matched(IReadOnlyList<SegmentMatch> Matches, SegmentComparisonPage FirstPage) : SegmentMatchResult;
    public sealed record NoTrack : SegmentMatchResult;
    public sealed record Cancelled : SegmentMatchResult;
    public sealed record Failed(string ErrorMessage) : SegmentMatchResult;
}

/// <summary>
/// Up to <see cref="Size"/> comparisons, and the matches skipped because their
/// session changed, is gone, or has no matched signals.
/// </summary>
public sealed record SegmentComparisonPage(
    IReadOnlyList<SegmentComparison> Comparisons,
    IReadOnlyList<SegmentMatch> UnavailableMatches)
{
    public const int Size = 10;
}

/// <summary>
/// Owns the saved-segment workflows: create, rename, and delete, matching a
/// selected range against every session, and the Strava connection and segment
/// fetch. Segment names are unique among the live segments of one session.
/// </summary>
public interface ISegmentCoordinator
{
    Task<SegmentChangeResult> CreateAsync(Guid sessionId, string name, double startSeconds, double endSeconds);

    Task<SegmentChangeResult> RenameAsync(Guid segmentId, string name);

    Task<SegmentChangeResult> DeleteAsync(Guid segmentId);

    /// <summary>
    /// Returns the error for a segment name in the session, or null when the
    /// name is valid. <paramref name="segmentId"/> is the renamed segment.
    /// </summary>
    string? ValidateName(Guid sessionId, string name, Guid? segmentId = null);

    /// <summary>
    /// Returns the first free "Segment N" name in the session.
    /// </summary>
    string NextName(Guid sessionId);

    /// <summary>
    /// Matches the source range against every session as the session's operation,
    /// and builds the first comparison page. A newer operation of the session cancels it.
    /// </summary>
    Task<SegmentMatchResult> MatchAsync(SegmentMatchSource source);

    /// <summary>Builds the comparisons of the first available matches.</summary>
    Task<SegmentComparisonPage> LoadComparisonPageAsync(
        IReadOnlyList<SegmentMatch> matches,
        double sourceDurationSeconds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the analysis input of a comparison, or null when its session changed
    /// or is gone.
    /// </summary>
    Task<SegmentComparisonDetail?> LoadComparisonDetailAsync(
        SegmentComparison comparison,
        CancellationToken cancellationToken);

    Task<StravaConnectionSnapshot> GetStravaConnectionAsync();

    string CreateStravaAuthorizationUrl(string clientId);

    /// <summary>
    /// Exchanges the code in the redirect URL the user pasted for tokens and
    /// stores the credentials. A failed request is an outcome.
    /// </summary>
    Task<StravaConnectResult> ConnectStravaAsync(StravaConnectRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Strava segment efforts inside the session. A failed request
    /// is an outcome; cancellation throws.
    /// </summary>
    Task<StravaSegmentFetchResult> FindStravaSegmentsAsync(
        long? sessionTimestamp,
        double? sessionDurationSeconds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the stored Strava credentials and tokens.
    /// </summary>
    Task ResetStravaAsync();
}

internal sealed class SegmentCoordinator(
    ISessionSegmentRepository repository,
    ISessionSegmentStoreWriter segmentStore,
    ISessionStore sessionStore,
    ISegmentMatcher matcher,
    ISessionContentReader contentReader,
    ISessionOperationRunner operationRunner,
    IBackgroundTaskRunner backgroundTaskRunner,
    IStravaSegmentService stravaService) : ISegmentCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SegmentCoordinator>();
    private static readonly SegmentChangeResult MissingSegment =
        new SegmentChangeResult.Failed("Saved segment no longer exists.");

    public async Task<SegmentChangeResult> CreateAsync(Guid sessionId, string name, double startSeconds, double endSeconds)
    {
        if (sessionStore.Get(sessionId) is null)
        {
            return new SegmentChangeResult.Failed("Session no longer exists.");
        }

        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds >= endSeconds)
        {
            return new SegmentChangeResult.Failed("Saved segment range is invalid.");
        }

        var trimmedName = name.Trim();
        if (ValidateName(sessionId, trimmedName) is { } error)
        {
            return new SegmentChangeResult.Failed(error);
        }

        var segment = new SessionSegment
        {
            SessionId = sessionId,
            Name = trimmedName,
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
        };
        return await WriteAsync("saved", async () =>
        {
            await repository.InsertAsync(segment);
            return segment;
        });
    }

    public async Task<SegmentChangeResult> RenameAsync(Guid segmentId, string name)
    {
        var trimmedName = name.Trim();
        if (segmentStore.Get(segmentId) is not { } existing)
        {
            return MissingSegment;
        }

        if (ValidateName(existing.SessionId, trimmedName, segmentId) is { } error)
        {
            return new SegmentChangeResult.Failed(error);
        }

        return await WriteAsync("renamed", () => repository.RenameAsync(segmentId, trimmedName));
    }

    public Task<SegmentChangeResult> DeleteAsync(Guid segmentId) =>
        WriteAsync("deleted", () => repository.DeleteAsync(segmentId));

    public string? ValidateName(Guid sessionId, string name, Guid? segmentId = null)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return "Segment name is required.";
        }

        return segmentStore.GetForSession(sessionId).Any(segment =>
            segment.Id != segmentId && StringComparer.Ordinal.Equals(segment.Name, trimmedName))
            ? "A saved segment with this name already exists."
            : null;
    }

    public string NextName(Guid sessionId)
    {
        var names = segmentStore.GetForSession(sessionId).Select(segment => segment.Name).ToHashSet(StringComparer.Ordinal);
        var index = 1;
        while (names.Contains($"Segment {index}"))
        {
            index++;
        }

        return $"Segment {index}";
    }

    public async Task<SegmentMatchResult> MatchAsync(SegmentMatchSource source)
    {
        using var operation = operationRunner.Start(source.SessionId, "matching segment");
        var cancellationToken = operation.CancellationToken;
        try
        {
            if (await LoadSourceAsync(source, cancellationToken) is not var (points, contentToken))
            {
                return new SegmentMatchResult.NoTrack();
            }

            var progress = new Progress(value => ReportMatchProgress(operation, value));
            var matches = await backgroundTaskRunner.RunAsync(
                () => matcher.FindMatchesAsync(
                    new SegmentMatchRequest(
                        points,
                        new SegmentMatchSourceContext(source.SessionId, source.Range, contentToken),
                        SegmentDtwMatchOptions.Default),
                    cancellationToken,
                    progress),
                cancellationToken);

            var sorted = matches
                .OrderBy(match => GetMatchedSegmentLocalTime(match))
                .ThenBy(match => match.NormalizedCostMeters)
                .ThenBy(match => match.SessionName, StringComparer.CurrentCulture)
                .ToArray();
            operation.Report("building comparison rows", 85);
            var firstPage = await LoadComparisonPageAsync(sorted, source.DurationSeconds, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return operation.IsCurrent
                ? new SegmentMatchResult.Matched(sorted.Except(firstPage.UnavailableMatches).ToArray(), firstPage)
                : new SegmentMatchResult.Cancelled();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SegmentMatchResult.Cancelled();
        }
        catch (Exception e)
        {
            logger.Error(e, "Segment matching failed");
            return new SegmentMatchResult.Failed($"Segment matching failed: {e.Message}");
        }
    }

    public async Task<SegmentComparisonPage> LoadComparisonPageAsync(
        IReadOnlyList<SegmentMatch> matches,
        double sourceDurationSeconds,
        CancellationToken cancellationToken)
    {
        var comparisons = new List<SegmentComparison>();
        var unavailable = new List<SegmentMatch>();
        foreach (var match in matches)
        {
            if (comparisons.Count == SegmentComparisonPage.Size)
            {
                break;
            }

            var snapshot = await contentReader.GetExactSnapshotAsync(
                match.SessionId,
                match.ContentToken,
                SessionContentSelection.All,
                cancellationToken);
            if (snapshot is not SessionContentSnapshotResult.Available { Snapshot.ProcessedTelemetry: { } telemetry } available)
            {
                unavailable.Add(match);
                continue;
            }

            var track = available.Snapshot.Track;
            var comparison = await backgroundTaskRunner.RunAsync(
                () => SegmentComparisonBuilder.Build(
                    match,
                    telemetry,
                    sourceDurationSeconds,
                    track,
                    GetTrackTimeline(track, telemetry)),
                cancellationToken);
            if (comparison is null)
            {
                unavailable.Add(match);
            }
            else
            {
                comparisons.Add(comparison);
            }
        }

        return new SegmentComparisonPage(comparisons, unavailable);
    }

    public async Task<SegmentComparisonDetail?> LoadComparisonDetailAsync(
        SegmentComparison comparison,
        CancellationToken cancellationToken)
    {
        var snapshot = await contentReader.GetExactSnapshotAsync(
            comparison.Match.SessionId,
            comparison.Match.ContentToken,
            SessionContentSelection.All,
            cancellationToken);
        return snapshot is SessionContentSnapshotResult.Available { Snapshot.ProcessedTelemetry: { } telemetry } available
            ? SegmentComparisonBuilder.BuildDetail(telemetry, available.Snapshot.Track)
            : null;
    }

    public Task<StravaConnectionSnapshot> GetStravaConnectionAsync() => stravaService.GetConnectionSnapshotAsync();

    public string CreateStravaAuthorizationUrl(string clientId) => stravaService.CreateAuthorizationUrl(clientId);

    public async Task<StravaConnectResult> ConnectStravaAsync(StravaConnectRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await stravaService.ConnectAsync(request, cancellationToken);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.Error(e, "Strava connection failed");
            return new StravaConnectResult.Failed(e.Message);
        }
    }

    public async Task<StravaSegmentFetchResult> FindStravaSegmentsAsync(
        long? sessionTimestamp,
        double? sessionDurationSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await stravaService.GetConnectionSnapshotAsync(cancellationToken);
            if (!connection.IsConnected)
            {
                return new StravaSegmentFetchResult.NotConnected();
            }

            if (sessionTimestamp is not { } timestamp || sessionDurationSeconds is not { } duration)
            {
                return new StravaSegmentFetchResult.MissingSessionTiming();
            }

            return await stravaService.FindSegmentsAsync(
                DateTimeOffset.FromUnixTimeSeconds(timestamp),
                duration,
                cancellationToken);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.Error(e, "Strava segments could not be loaded");
            return new StravaSegmentFetchResult.Failed(e.Message);
        }
    }

    public Task ResetStravaAsync() => stravaService.ResetCredentialsAsync();

    private async Task<(IReadOnlyList<TrackPoint> Points, SessionContentToken Token)?> LoadSourceAsync(
        SegmentMatchSource source,
        CancellationToken cancellationToken)
    {
        if (await contentReader.GetSessionAsync(source.SessionId, cancellationToken) is not { } session)
        {
            return null;
        }

        var snapshot = await contentReader.GetExactSnapshotAsync(
            source.SessionId,
            session.ContentToken,
            SessionContentSelection.Track,
            cancellationToken);
        if (snapshot is not SessionContentSnapshotResult.Available { Snapshot.Track: { } track })
        {
            return null;
        }

        var start = source.TrackTimeline.OriginSeconds + source.Range.StartSeconds;
        var end = source.TrackTimeline.OriginSeconds + source.Range.EndSeconds;
        var points = track.Where(point => point.Time >= start && point.Time <= end).ToArray();
        return points.Length >= 2 ? (points, session.ContentToken) : null;
    }

    private static DateTime GetMatchedSegmentLocalTime(SegmentMatch match) =>
        match.SessionTimestamp is { } timestamp
            ? DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime.AddSeconds(match.StartSeconds)
            : DateTime.MaxValue;

    private static TrackTimeRange? GetTrackTimeline(IReadOnlyList<TrackPoint>? points, TelemetryData telemetry)
    {
        if (double.IsFinite(telemetry.Metadata.Timestamp) &&
            double.IsFinite(telemetry.Metadata.Duration) &&
            telemetry.Metadata.Duration > 0)
        {
            return new TrackTimeRange(telemetry.Metadata.Timestamp, telemetry.Metadata.Duration);
        }

        if (points is null || points.Count < 2)
        {
            return null;
        }

        var first = points.FirstOrDefault(point => double.IsFinite(point.Time));
        var last = points.LastOrDefault(point => double.IsFinite(point.Time));
        var duration = last?.Time - first?.Time;
        return first is not null && duration is > 0 && double.IsFinite(duration.Value)
            ? new TrackTimeRange(first.Time, duration.Value)
            : null;
    }

    private static void ReportMatchProgress(SessionOperation operation, SegmentMatchProgress progress)
    {
        switch (progress)
        {
            case SegmentMatchProgress.EnumeratingSessions enumerating:
                operation.Report(
                    $"enumerating session {Math.Max(1, enumerating.CurrentSession)}/{Math.Max(1, enumerating.TotalSessions)}",
                    5 + 30 * Fraction(enumerating.CurrentSession, enumerating.TotalSessions));
                break;
            case SegmentMatchProgress.MatchingSessions matching:
                operation.Report(
                    $"matching session {Math.Clamp(matching.CompletedSessions + 1, 1, Math.Max(1, matching.TotalSessions))}/{Math.Max(1, matching.TotalSessions)}",
                    35 + 50 * Fraction(matching.CompletedSessions + Math.Clamp(matching.CurrentSessionFraction, 0, 1), matching.TotalSessions));
                break;
            default:
                operation.Report("loading sessions", 0);
                break;
        }
    }

    private static double Fraction(double current, int total) => total <= 0 ? 0 : Math.Clamp(current / total, 0, 1);

    private async Task<SegmentChangeResult> WriteAsync(string action, Func<Task<SessionSegment?>> write)
    {
        try
        {
            if (await write() is not { } segment)
            {
                return MissingSegment;
            }

            await segmentStore.PublishSegmentsAsync([segment]);
            return new SegmentChangeResult.Applied();
        }
        catch (Exception e)
        {
            logger.Error(e, "Saved segment could not be {Action}", action);
            return new SegmentChangeResult.Failed($"Saved segment could not be {action}: {e.Message}");
        }
    }

    private sealed class Progress(Action<SegmentMatchProgress> report) : IProgress<SegmentMatchProgress>
    {
        public void Report(SegmentMatchProgress value) => report(value);
    }
}
