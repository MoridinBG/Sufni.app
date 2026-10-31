using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services.Matching;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.Services.Matching;

public class SegmentMatcherTests
{
    private readonly TestSessionContentReader reader = new();

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public async Task FindMatchesAsync_ExpressesAlignedCandidateMatchesInTelemetryTime(int gpsOffset)
    {
        var candidate = AddSession("Aligned candidate", 2000, BuildLineTrack(2000 + gpsOffset, 0, 400));
        candidate = candidate with { ContentToken = candidate.ContentToken with { GpsOffsetSeconds = gpsOffset } };
        ReplaceSession(candidate);

        var match = Assert.Single(await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions()), TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Id, match.SessionId);
        Assert.Equal(100, match.StartSeconds, precision: 6);
        Assert.Equal(300, match.EndSeconds, precision: 6);
        Assert.Equal(new SegmentTimeMapPoint(0, 100), match.TimeMap[0]);
        Assert.Equal(new SegmentTimeMapPoint(200, 300), match.TimeMap[^1]);
    }

    [Fact]
    public async Task FindMatchesAsync_FindsCandidateAndSkipsOverlappingSourceWindow()
    {
        var source = AddSession("Source", timestamp: 1000, BuildLineTrack(1000, 0, 400));
        var candidate = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        AddSession("Short", timestamp: 3000, BuildLineTrack(3000, 0, 40));
        var progress = new RecordingProgress<SegmentMatchProgress>();

        var matches = await new SegmentMatcher(reader).FindMatchesAsync(
            new SegmentMatchRequest(
                BuildLineTrack(1000, 100, 300),
                new SegmentMatchSourceContext(source.Id, new TelemetryTimeRange(100, 300), source.ContentToken),
                CreateOptions()),
            TestContext.Current.CancellationToken,
            progress);

        var match = Assert.Single(matches);
        Assert.Equal(candidate.Id, match.SessionId);
        Assert.Equal("Candidate", match.SessionName);
        Assert.Equal(100, match.StartSeconds, precision: 6);
        Assert.Equal(300, match.EndSeconds, precision: 6);
        Assert.Equal(0, match.NormalizedCostMeters, precision: 6);
        Assert.Equal(new SegmentTimeMapPoint(100, 100), match.TimeMap[0]);
        Assert.Equal(new SegmentTimeMapPoint(300, 300), match.TimeMap[^1]);
        Assert.Equal(candidate.ContentToken, match.ContentToken);
        Assert.Contains(progress.Reports, item => item is SegmentMatchProgress.EnumeratingSessions { CurrentSession: 3, TotalSessions: 3 });
        Assert.Contains(progress.Reports, item => item is SegmentMatchProgress.MatchingSessions { CompletedSessions: 2, TotalSessions: 2 });
    }

    [Fact]
    public async Task FindMatchesAsync_ReportsMatchingProgressThatNeverMovesBackwards()
    {
        AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 500));
        var progress = new RecordingProgress<SegmentMatchProgress>();

        var match = Assert.Single(await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(BuildLineTrack(1000, 120, 320), CreateLooseOptions()),
            TestContext.Current.CancellationToken,
            progress));

        Assert.Equal(120, match.StartSeconds, precision: 6);
        Assert.Equal(320, match.EndSeconds, precision: 6);
        var overall = progress.Reports
            .OfType<SegmentMatchProgress.MatchingSessions>()
            .Select(item => item.CompletedSessions + item.CurrentSessionFraction)
            .ToList();
        Assert.Contains(overall, value => value is > 0 and < 1);
        Assert.Equal(overall.Order(), overall);
    }

    [Fact]
    public async Task FindMatchesAsync_ScoresAnOffsetPathByItsDistance()
    {
        AddSession("Offset", timestamp: 2000, BuildLineTrack(2000, 0, 400, y: 2));

        var match = Assert.Single(await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions()),
            TestContext.Current.CancellationToken));

        Assert.Equal(2, match.NormalizedCostMeters, precision: 6);
    }

    [Fact]
    public async Task FindMatchesAsync_IgnoresAPathRiddenInTheOppositeDirection()
    {
        AddSession("Reversed", timestamp: 2000, BuildReversedLineTrack(2000, 0, 400));

        var matches = await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions()),
            TestContext.Current.CancellationToken);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindMatchesAsync_WithParallelScanningMatchesSerialResult()
    {
        AddSession("Exact", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        AddSession("Offset", timestamp: 3000, BuildLineTrack(3000, 0, 400, y: 1));
        AddSession("Later exact", timestamp: 4000, BuildLineTrack(4000, 0, 400));
        var source = BuildLineTrack(1000, 100, 300);

        var serial = await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(source, CreateOptions()),
            TestContext.Current.CancellationToken);
        var parallel = await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(source, CreateOptions(maxDegreeOfParallelism: 4)),
            TestContext.Current.CancellationToken);

        Assert.Equal(serial, parallel, MatchComparer.Instance);
    }

    [Fact]
    public async Task FindMatchesAsync_KeepsCandidateOrderWhenTrackLoadsCompleteOutOfOrder()
    {
        var first = AddSession("Same", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var second = AddSession("Same", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var secondLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.TrackLoader = async (sessionId, _) =>
        {
            if (sessionId == first.Id)
            {
                await secondLoaded.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            }
            else
            {
                secondLoaded.TrySetResult();
            }

            return reader.Tracks.GetValueOrDefault(sessionId);
        };

        var matches = await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions(maxDegreeOfParallelism: 4)),
            TestContext.Current.CancellationToken);

        Assert.Equal([first.Id, second.Id], matches.Select(match => match.SessionId));
    }

    [Fact]
    public async Task FindMatchesAsync_ReturnsNothing_WhenTheSourceChangedDuringMatching()
    {
        var source = AddSession("Source", timestamp: 1000, BuildLineTrack(1000, 0, 400));
        AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var staleToken = source.ContentToken with { ProcessedTelemetryRevision = 0 };

        var matches = await new SegmentMatcher(reader).FindMatchesAsync(
            new SegmentMatchRequest(
                BuildLineTrack(1000, 100, 300),
                new SegmentMatchSourceContext(source.Id, new TelemetryTimeRange(100, 300), staleToken),
                CreateOptions()),
            TestContext.Current.CancellationToken);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindMatchesAsync_ReusesPreparedPathForUnchangedContent()
    {
        var session = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var matcher = new SegmentMatcher(reader);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());

        var first = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        var second = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(first, second, MatchComparer.Instance);
        Assert.Equal(1, reader.TrackReads(session.Id));
    }

    [Fact]
    public async Task FindMatchesAsync_PreparesPathsPerSegmentLocation()
    {
        var session = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 500));
        var matcher = new SegmentMatcher(reader);
        var firstRequest = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());
        var otherRequest = firstRequest with { SegmentPoints = BuildLineTrack(1000, 200, 400) };

        var first = await matcher.FindMatchesAsync(firstRequest, TestContext.Current.CancellationToken);
        _ = await matcher.FindMatchesAsync(otherRequest, TestContext.Current.CancellationToken);
        var again = await matcher.FindMatchesAsync(firstRequest, TestContext.Current.CancellationToken);

        Assert.Equal(first, again, MatchComparer.Instance);
        Assert.Equal(2, reader.TrackReads(session.Id));
    }

    [Fact]
    public async Task FindMatchesAsync_RereadsTheTrackOnlyWhenContentChanges()
    {
        var session = AddSession("Original", timestamp: 3000, BuildLineTrack(3000, 0, 400));
        var matcher = new SegmentMatcher(reader);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());

        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        ReplaceSession(session with { Name = "Renamed" });
        var renamed = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        ReplaceSession(session with { ContentToken = session.ContentToken with { TrackProjectionRevision = 2 } });
        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("Renamed", Assert.Single(renamed).SessionName);
        Assert.Equal(2, reader.TrackReads(session.Id));
    }

    [Fact]
    public async Task FindMatchesAsync_DoesNotKeepAPathReadUnderAStaleToken()
    {
        var session = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var current = session with { ContentToken = session.ContentToken with { TrackProjectionRevision = 2 } };
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.TrackLoader = async (sessionId, _) =>
        {
            if (reader.Sessions[0] == session)
            {
                loadStarted.TrySetResult();
                await releaseLoad.Task;
            }

            return reader.Tracks.GetValueOrDefault(sessionId);
        };
        var matcher = new SegmentMatcher(reader);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());

        var staleRun = matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        ReplaceSession(current);
        releaseLoad.SetResult();
        var staleMatches = await staleRun;
        var currentMatches = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(staleMatches);
        Assert.Equal(current.ContentToken, Assert.Single(currentMatches).ContentToken);
        Assert.Equal(1, reader.TrackReads(session.Id, current.ContentToken));
    }

    [Fact]
    public async Task FindMatchesAsync_ConcurrentRequestsReadTheTrackOnce()
    {
        var session = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.TrackLoader = async (sessionId, _) =>
        {
            loadStarted.TrySetResult();
            await releaseLoad.Task;
            return reader.Tracks.GetValueOrDefault(sessionId);
        };
        var matcher = new SegmentMatcher(reader);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());

        var first = matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var second = matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        releaseLoad.SetResult();

        Assert.Equal(await first, await second, MatchComparer.Instance);
        Assert.Equal(1, reader.TrackReads(session.Id));
    }

    [Fact]
    public async Task FindMatchesAsync_EvictsPreparedPathsBeyondTheWeightLimit()
    {
        var session = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var changed = session with { ContentToken = session.ContentToken with { TrackProjectionRevision = 2 } };
        var matcher = new SegmentMatcher(reader, preparedPathRetentionLimitBytes: 1_500);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());

        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        ReplaceSession(changed);
        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        ReplaceSession(session);
        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, reader.TrackReads(session.Id, session.ContentToken));
    }

    [Fact]
    public async Task FindMatchesAsync_DoesNotKeepAPathLargerThanTheWeightLimit()
    {
        var session = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var matcher = new SegmentMatcher(reader, preparedPathRetentionLimitBytes: 1);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());

        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, reader.TrackReads(session.Id));
    }

    [Fact]
    public async Task FindMatchesAsync_DoesNotKeepAMissingTrack()
    {
        var session = AddSession("Missing", timestamp: 2000, track: null);
        var matcher = new SegmentMatcher(reader);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());

        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        _ = await matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, reader.TrackReads(session.Id));
    }

    [Fact]
    public async Task FindMatchesAsync_CancelledRequestDoesNotCancelASharedTrackRead()
    {
        var session = AddSession("Candidate", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.TrackLoader = async (sessionId, _) =>
        {
            loadStarted.TrySetResult();
            await releaseLoad.Task;
            return reader.Tracks.GetValueOrDefault(sessionId);
        };
        var matcher = new SegmentMatcher(reader);
        var request = CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions());
        using var cancellation = new CancellationTokenSource();

        var cancelled = matcher.FindMatchesAsync(request, cancellation.Token);
        await loadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var surviving = matcher.FindMatchesAsync(request, TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        releaseLoad.SetResult();

        Assert.NotEmpty(await surviving);
        Assert.Equal(1, reader.TrackReads(session.Id));
    }

    [Fact]
    public async Task FindMatchesAsync_StopsWhenCancelledDuringTrackLoading()
    {
        AddSession("Pending", timestamp: 2000, BuildLineTrack(2000, 0, 400));
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.TrackLoader = async (sessionId, _) =>
        {
            loadStarted.TrySetResult();
            await releaseLoad.Task;
            return reader.Tracks.GetValueOrDefault(sessionId);
        };
        using var cancellation = new CancellationTokenSource();

        var match = new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(BuildLineTrack(1000, 100, 300), CreateOptions()),
            cancellation.Token);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => match.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        releaseLoad.SetResult();
    }

    [Fact]
    public async Task FindMatchesAsync_ReturnsNothingForATooShortSegmentWithoutReadingSessions()
    {
        var matches = await new SegmentMatcher(reader).FindMatchesAsync(
            CreateRequest(BuildLineTrack(1000, 0, 20), CreateOptions()),
            TestContext.Current.CancellationToken);

        Assert.Empty(matches);
        Assert.Equal(0, reader.GetSessionsCalls);
    }

    private SegmentMatchRequest CreateRequest(IReadOnlyList<TrackPoint> points, SegmentDtwMatchOptions options)
    {
        var source = AddSession("Source", (long)points[0].Time, track: null);
        return new SegmentMatchRequest(
            points,
            new SegmentMatchSourceContext(
                source.Id,
                new TelemetryTimeRange(0, points[^1].Time - points[0].Time),
                source.ContentToken),
            options);
    }

    private void ReplaceSession(SessionCatalogItem replacement) =>
        reader.Sessions = reader.Sessions
            .Select(session => session.Id == replacement.Id ? replacement : session)
            .ToArray();

    private SessionCatalogItem AddSession(string name, long timestamp, IReadOnlyList<TrackPoint>? track)
    {
        var session = new SessionCatalogItem(
            Guid.NewGuid(),
            name,
            timestamp,
            DurationSeconds: null,
            new SessionContentToken(1, 1, null, null, timestamp, null, 0));
        reader.Sessions = [.. reader.Sessions, session];
        if (track is not null)
        {
            reader.Tracks[session.Id] = track;
        }

        return session;
    }

    private static List<TrackPoint> BuildLineTrack(long timestamp, double startX, double endX, double y = 0)
    {
        var points = new List<TrackPoint>();
        for (var x = startX; x <= endX; x += 10)
        {
            points.Add(new TrackPoint(timestamp + x, x, y, elevation: null));
        }

        return points;
    }

    private static List<TrackPoint> BuildReversedLineTrack(long timestamp, double startX, double endX)
    {
        var points = new List<TrackPoint>();
        for (var x = startX; x <= endX; x += 10)
        {
            points.Add(new TrackPoint(timestamp + x, endX - x, 0, elevation: null));
        }

        return points;
    }

    private static SegmentDtwMatchOptions CreateOptions(int maxDegreeOfParallelism = 1) => new(
        ResampleStepMeters: 10,
        MinSegmentLengthMeters: 50,
        MaxPointDistanceMeters: 5,
        MaxNormalizedCostMeters: 5,
        CandidateLengthToleranceRatio: 0.1,
        MaxDegreeOfParallelism: maxDegreeOfParallelism);

    private static SegmentDtwMatchOptions CreateLooseOptions() => new(
        ResampleStepMeters: 10,
        MinSegmentLengthMeters: 50,
        MaxPointDistanceMeters: 40,
        MaxNormalizedCostMeters: 40,
        CandidateLengthToleranceRatio: 0.35,
        MaxDegreeOfParallelism: 1);

    private sealed class MatchComparer : IEqualityComparer<SegmentMatch>
    {
        public static MatchComparer Instance { get; } = new();

        public bool Equals(SegmentMatch? x, SegmentMatch? y) =>
            x is not null && y is not null &&
            (x with { TimeMap = [] }) == (y with { TimeMap = [] }) &&
            x.TimeMap.SequenceEqual(y.TimeMap);

        public int GetHashCode(SegmentMatch obj) => obj.SessionId.GetHashCode();
    }
}
