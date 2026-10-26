using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services.Matching;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;

[InProcess]
[MemoryDiagnoser]
public class SegmentMatcherBenchmarks
{
    private InMemorySessionContentReader singleLongCandidateReader = null!;
    private SegmentMatchRequest singleLongCandidateRequest = null!;
    private SegmentMatcher singleLongCandidateWarmMatcher = null!;
    private InMemorySessionContentReader manyCandidatesReader = null!;
    private SegmentMatchRequest manyCandidatesRequest = null!;
    private SegmentMatcher manyCandidatesWarmMatcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        singleLongCandidateRequest = new SegmentMatchRequest(
            BuildLineTrack(timestamp: 1_000_000, startX: 1_235, endX: 1_735, step: 5),
            Source: null,
            CreateDefaultBenchmarkOptions());
        singleLongCandidateReader = new InMemorySessionContentReader(
        [
            CreateSession("single-match", timestamp: 2_000_000, BuildLineTrack(2_000_000, 0, 4_000, 5)),
            CreateSession("single-short", timestamp: 3_000_000, BuildLineTrack(3_000_000, 0, 100, 5)),
        ]);
        singleLongCandidateWarmMatcher = new SegmentMatcher(singleLongCandidateReader);

        var manyCandidateEntries = new List<SessionEntry>
        {
            CreateSession("many-match", timestamp: 4_000_000, BuildLineTrack(4_000_000, 0, 3_000, 5)),
        };

        for (var i = 0; i < 4; i++)
        {
            manyCandidateEntries.Add(CreateSession(
                $"many-translated-{i:00}",
                timestamp: 4_100_000 + i,
                BuildLineTrack(4_100_000 + i, 0, 3_000, 5, yOffset: 500)));
        }

        for (var i = 0; i < 5; i++)
        {
            manyCandidateEntries.Add(CreateSession(
                $"many-reversed-{i:00}",
                timestamp: 4_200_000 + i,
                BuildLineTrack(4_200_000 + i, 3_000, 0, -5)));
        }

        manyCandidatesRequest = new SegmentMatchRequest(
            BuildLineTrack(timestamp: 5_000_000, startX: 1_235, endX: 1_635, step: 5),
            Source: null,
            CreateDefaultBenchmarkOptions());
        manyCandidatesReader = new InMemorySessionContentReader(manyCandidateEntries);
        manyCandidatesWarmMatcher = new SegmentMatcher(manyCandidatesReader);

        _ = singleLongCandidateWarmMatcher
            .FindMatchesAsync(singleLongCandidateRequest)
            .GetAwaiter()
            .GetResult();
        _ = manyCandidatesWarmMatcher
            .FindMatchesAsync(manyCandidatesRequest)
            .GetAwaiter()
            .GetResult();
    }

    [Benchmark]
    public async Task<int> SingleLongCandidate_ColdCache_DefaultOptions()
    {
        var matcher = new SegmentMatcher(singleLongCandidateReader);
        var matches = await matcher.FindMatchesAsync(singleLongCandidateRequest);
        return matches.Count;
    }

    [Benchmark]
    public async Task<int> SingleLongCandidate_WarmCache_DefaultOptions()
    {
        var matches = await singleLongCandidateWarmMatcher.FindMatchesAsync(singleLongCandidateRequest);
        return matches.Count;
    }

    [Benchmark]
    public async Task<int> ManyCandidates_ColdCache_DefaultOptions()
    {
        var matcher = new SegmentMatcher(manyCandidatesReader);
        var matches = await matcher.FindMatchesAsync(manyCandidatesRequest);
        return matches.Count;
    }

    [Benchmark]
    public async Task<int> ManyCandidates_WarmCache_DefaultOptions()
    {
        var matches = await manyCandidatesWarmMatcher.FindMatchesAsync(manyCandidatesRequest);
        return matches.Count;
    }

    private static SegmentDtwMatchOptions CreateDefaultBenchmarkOptions()
    {
        var defaults = SegmentDtwMatchOptions.Default;
        return defaults with { MaxDegreeOfParallelism = Environment.ProcessorCount };
    }

    private static SessionEntry CreateSession(
        string name,
        long timestamp,
        IReadOnlyList<TrackPoint> track)
    {
        var id = Guid.CreateVersion7();
        return new SessionEntry(
            new SessionCatalogItem(
                id,
                name,
                timestamp,
                DurationSeconds: null,
                new SessionContentToken(
                    ProcessedTelemetryRevision: 0,
                    TrackProjectionRevision: timestamp,
                    FullTrackId: null,
                    FullTrackPointsRevision: null,
                    Timestamp: timestamp,
                    DurationSeconds: null,
                    GpsOffsetSeconds: 0)),
            track);
    }

    private static List<TrackPoint> BuildLineTrack(
        long timestamp,
        double startX,
        double endX,
        double step,
        double yOffset = 0)
    {
        var points = new List<TrackPoint>();
        for (var x = startX; step > 0 ? x <= endX : x >= endX; x += step)
        {
            var elapsed = Math.Abs(x - startX);
            points.Add(new TrackPoint(timestamp + elapsed, x, yOffset, elevation: null));
        }

        return points;
    }

    private sealed record SessionEntry(
        SessionCatalogItem Session,
        IReadOnlyList<TrackPoint> Track);

    private sealed class InMemorySessionContentReader(
        IReadOnlyList<SessionEntry> entries) : ISessionContentReader
    {
        private readonly IReadOnlyDictionary<Guid, SessionEntry> byId =
            entries.ToDictionary(entry => entry.Session.Id);

        public Task<IReadOnlyList<SessionCatalogItem>> GetSessionsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SessionCatalogItem>>(
                entries.Select(entry => entry.Session).ToArray());
        }

        public Task<SessionCatalogItem?> GetSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(byId.TryGetValue(sessionId, out var entry)
                ? entry.Session
                : null);
        }

        public Task<SessionContentSnapshotResult> GetExactSnapshotAsync(
            Guid sessionId,
            SessionContentToken expectedToken,
            SessionContentSelection selection,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(sessionId, out var entry))
            {
                return Task.FromResult<SessionContentSnapshotResult>(
                    new SessionContentSnapshotResult.Missing());
            }

            if (entry.Session.ContentToken != expectedToken)
            {
                return Task.FromResult<SessionContentSnapshotResult>(
                    new SessionContentSnapshotResult.Stale());
            }

            if (selection.HasFlag(SessionContentSelection.ProcessedTelemetry))
            {
                return Task.FromResult<SessionContentSnapshotResult>(
                    new SessionContentSnapshotResult.Missing());
            }

            var track = selection.HasFlag(SessionContentSelection.Track)
                ? entry.Track
                : null;
            return Task.FromResult<SessionContentSnapshotResult>(
                new SessionContentSnapshotResult.Available(
                    new SessionContentSnapshot(
                        expectedToken,
                        selection,
                        ProcessedTelemetry: null,
                        track)));
        }
    }
}
