using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.Telemetry.Caching;

using Sufni.App.Segments.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using static Sufni.App.Segments.Services.Matching.SegmentDtw;

namespace Sufni.App.Segments.Services.Matching;

/// <summary>
/// Finds where a GPS segment was ridden in every session. Loads candidate tracks
/// through the session content reader and keeps their prepared paths in a bounded
/// cache keyed by content; the DTW scoring itself is <see cref="SegmentDtw"/>.
/// </summary>
public interface ISegmentMatcher
{
    Task<IReadOnlyList<SegmentMatch>> FindMatchesAsync(
        SegmentMatchRequest request,
        CancellationToken cancellationToken = default,
        IProgress<SegmentMatchProgress>? progress = null);
}

internal sealed class SegmentMatcher : ISegmentMatcher
{
    private const long PreparedPathRetentionLimitBytes = 64L * 1024 * 1024;
    private const int CandidateTrackLoadDop = 4;

    private readonly ISessionContentReader contentReader;
    private readonly SingleFlightLruCache<PreparedCandidatePathKey, TrackPath?> preparedCandidatePaths;

    public SegmentMatcher(ISessionContentReader contentReader)
        : this(contentReader, PreparedPathRetentionLimitBytes)
    {
    }

    internal SegmentMatcher(
        ISessionContentReader contentReader,
        long preparedPathRetentionLimitBytes)
    {
        this.contentReader = contentReader;
        preparedCandidatePaths = new SingleFlightLruCache<PreparedCandidatePathKey, TrackPath?>(
            preparedPathRetentionLimitBytes,
            static path => checked((long)path!.Samples.Count * 32),
            static path => path is not null);
    }

    internal long PreparedPathRetainedWeightBytes => preparedCandidatePaths.RetainedWeight;
    internal int PreparedPathCount => preparedCandidatePaths.Count;

    public async Task<IReadOnlyList<SegmentMatch>> FindMatchesAsync(
        SegmentMatchRequest request,
        CancellationToken cancellationToken = default,
        IProgress<SegmentMatchProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = NormalizeOptions(request.Options);
        if (!TryBuildSourcePath(request.SegmentPoints, options, out var source))
        {
            return [];
        }

        var candidates = await LoadCandidateTracksAsync(source, options, cancellationToken, progress);
        var matches = new List<SegmentMatch>();

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
        };

        // Candidates are processed one at a time; the heavy window scans inside
        // FindBestMatch fan out across cores instead, so one long session cannot
        // pin the tail of the search to a single thread.
        for (var index = 0; index < candidates.Tracks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reporter = progress is null
                ? null
                : new MatchingSessionsReporter(progress, index, candidates.Tracks.Count);
            reporter?.Report(0);

            var match = FindBestMatch(source, candidates.Tracks[index], request, options, parallelOptions, reporter is null ? null : reporter.Report);
            if (match is not null)
            {
                matches.Add(match);
            }

            progress?.Report(new SegmentMatchProgress.MatchingSessions(index + 1, candidates.Tracks.Count));
        }

        if (request.Source is { } sourceContext &&
            await contentReader.GetExactSnapshotAsync(
                sourceContext.SessionId,
                sourceContext.ContentToken,
                SessionContentSelection.None,
                cancellationToken) is not SessionContentSnapshotResult.Available)
        {
            return [];
        }

        return matches
            .OrderBy(match => match.NormalizedCostMeters)
            .ThenBy(match => match.MedianErrorMeters)
            .ThenBy(match => candidates.SessionSortKeys.TryGetValue(match.SessionId, out var key) ? key.Timestamp : long.MaxValue)
            .ThenBy(match => match.SessionName, StringComparer.Ordinal)
            .ToList();
    }

    private static SegmentDtwMatchOptions NormalizeOptions(SegmentDtwMatchOptions options)
    {
        var defaults = SegmentDtwMatchOptions.Default;
        return new SegmentDtwMatchOptions(
            ResampleStepMeters: options.ResampleStepMeters > 0 ? options.ResampleStepMeters : defaults.ResampleStepMeters,
            MinSegmentLengthMeters: options.MinSegmentLengthMeters > 0 ? options.MinSegmentLengthMeters : defaults.MinSegmentLengthMeters,
            MaxPointDistanceMeters: options.MaxPointDistanceMeters > 0 ? options.MaxPointDistanceMeters : defaults.MaxPointDistanceMeters,
            MaxNormalizedCostMeters: options.MaxNormalizedCostMeters > 0 ? options.MaxNormalizedCostMeters : defaults.MaxNormalizedCostMeters,
            CandidateLengthToleranceRatio: options.CandidateLengthToleranceRatio >= 0 ? options.CandidateLengthToleranceRatio : defaults.CandidateLengthToleranceRatio,
            MaxDegreeOfParallelism: options.MaxDegreeOfParallelism > 0 ? options.MaxDegreeOfParallelism : defaults.MaxDegreeOfParallelism);
    }

    private async Task<CandidateLoadResult> LoadCandidateTracksAsync(
        TrackPath source,
        SegmentDtwMatchOptions options,
        CancellationToken cancellationToken,
        IProgress<SegmentMatchProgress>? progress)
    {
        progress?.Report(new SegmentMatchProgress.LoadingSessions());
        var sessions = await contentReader.GetSessionsAsync(cancellationToken);
        var sortKeys = new Dictionary<Guid, SessionSortKey>(sessions.Count);

        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            cancellationToken.ThrowIfCancellationRequested();
            sortKeys[session.Id] = new SessionSortKey(session.Timestamp ?? long.MaxValue);
        }

        var candidateTracks = new CandidateTrack?[sessions.Count];
        var progressGate = new object();
        var completed = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = CandidateTrackLoadDop,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, sessions.Count), parallelOptions, async (index, ct) =>
        {
            var session = sessions[index];
            var path = await GetPreparedCandidatePathAsync(
                session,
                source.Reference,
                options.ResampleStepMeters,
                ct);
            if (path is null || path.DistanceMeters < source.DistanceMeters)
            {
                ReportEnumerationProgress();
                return;
            }

            candidateTracks[index] = new CandidateTrack(session, path);
            ReportEnumerationProgress();

            void ReportEnumerationProgress()
            {
                lock (progressGate)
                {
                    completed++;
                    progress?.Report(new SegmentMatchProgress.EnumeratingSessions(completed, sessions.Count));
                }
            }
        });

        var tracks = new List<CandidateTrack>(sessions.Count);
        foreach (var track in candidateTracks)
        {
            if (track is not null)
            {
                tracks.Add(track);
            }
        }

        return new CandidateLoadResult(tracks, sortKeys);
    }

    private async Task<TrackPath?> GetPreparedCandidatePathAsync(
        SessionCatalogItem session,
        LocalReference reference,
        double resampleStepMeters,
        CancellationToken cancellationToken)
    {
        var key = PreparedCandidatePathKey.Create(
            session.Id,
            session.ContentToken,
            reference,
            resampleStepMeters);
        return await preparedCandidatePaths.GetOrAddAsync(
            key,
            (_, ct) => BuildCandidatePathAsync(
                session.Id,
                session.ContentToken,
                reference,
                resampleStepMeters,
                ct),
            cancellationToken);
    }

    private async Task<TrackPath?> BuildCandidatePathAsync(
        Guid sessionId,
        SessionContentToken contentToken,
        LocalReference reference,
        double resampleStepMeters,
        CancellationToken cancellationToken)
    {
        var result = await contentReader.GetExactSnapshotAsync(
            sessionId,
            contentToken,
            SessionContentSelection.Track,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var points = result is SessionContentSnapshotResult.Available available
            ? available.Snapshot.Track
            : null;
        return points is not null && points.Count >= 2 &&
            TryBuildPath(points, reference, resampleStepMeters, out var path)
                ? path
                : null;
    }

    private readonly record struct PreparedCandidatePathKey(
        Guid SessionId,
        SessionContentToken ContentToken,
        long ResampleStepBits,
        long OriginXBits,
        long OriginYBits,
        long ScaleBits)
    {
        public static PreparedCandidatePathKey Create(
            Guid sessionId,
            SessionContentToken contentToken,
            LocalReference reference,
            double resampleStepMeters) =>
            new(
                sessionId,
                contentToken,
                BitConverter.DoubleToInt64Bits(resampleStepMeters),
                BitConverter.DoubleToInt64Bits(reference.OriginX),
                BitConverter.DoubleToInt64Bits(reference.OriginY),
                BitConverter.DoubleToInt64Bits(reference.Scale));
    }
    private sealed record CandidateLoadResult(
        IReadOnlyList<CandidateTrack> Tracks,
        IReadOnlyDictionary<Guid, SessionSortKey> SessionSortKeys);
    private readonly record struct SessionSortKey(long Timestamp);
    // Serializes MatchingSessions reports for one session and drops sub-percent
    // updates so parallel workers can tick progress cheaply without flooding the
    // UI or reporting backwards.
    private sealed class MatchingSessionsReporter(
        IProgress<SegmentMatchProgress> progress,
        int completedSessions,
        int totalSessions)
    {
        private readonly System.Threading.Lock gate = new();
        private int lastReportedPercent = -1;

        public void Report(double currentSessionFraction)
        {
            var percent = (int)(Math.Clamp(currentSessionFraction, 0, 1) * 100);
            lock (gate)
            {
                if (percent <= lastReportedPercent)
                {
                    return;
                }

                lastReportedPercent = percent;
                progress.Report(new SegmentMatchProgress.MatchingSessions(
                    completedSessions,
                    totalSessions,
                    percent / 100d));
            }
        }
    }
}
