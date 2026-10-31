using Sufni.App.Shared.Models;
using Sufni.App.Shared.Stores;
using NSubstitute;

using Sufni.App.Segments.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Services.Matching;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
namespace Sufni.App.Tests.Segments.Coordinators;

public class SegmentCoordinatorTests
{
    private readonly ISessionSegmentRepository repository = Substitute.For<ISessionSegmentRepository>();
    private readonly ISessionSegmentStoreWriter segmentStore = Substitute.For<ISessionSegmentStoreWriter>();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly ISegmentMatcher matcher = Substitute.For<ISegmentMatcher>();
    private readonly TestSessionContentReader contentReader = new();
    private readonly SessionOperationRunner operationRunner = new();
    private readonly IStravaSegmentService stravaService = Substitute.For<IStravaSegmentService>();
    private readonly Guid sourceId = Guid.NewGuid();
    private readonly SessionSnapshot session = TestSnapshots.Session(id: Guid.NewGuid());

    public SegmentCoordinatorTests()
    {
        sessionStore.Get(session.Id).Returns(session);
        segmentStore.GetForSession(session.Id).Returns([new SessionSegmentSnapshot(Guid.NewGuid(), session.Id, "Segment 1", 0, 1)]);
    }

    [Fact]
    public async Task CreateAsync_PublishesInsertedSegment()
    {
        var result = await CreateCoordinator().CreateAsync(session.Id, " climb ", 2, 5);

        Assert.IsType<SegmentChangeResult.Applied>(result);
        await repository.Received(1).InsertAsync(Arg.Is<SessionSegment>(segment =>
            segment.SessionId == session.Id && segment.Name == "climb" && segment.StartSeconds == 2 && segment.EndSeconds == 5));
        await segmentStore.Received(1).PublishSegmentsAsync(Arg.Is<IReadOnlyCollection<SessionSegmentSnapshot>>(segments =>
            segments.Single().Name == "climb"), Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0));
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ReturnsInvalidWithoutWriting()
    {
        var result = await CreateCoordinator().CreateAsync(session.Id, "Segment 1", 2, 5);

        Assert.IsType<SegmentChangeResult.Invalid>(result);
        await repository.DidNotReceive().InsertAsync(Arg.Any<SessionSegment>());
    }

    [Fact]
    public async Task DeleteAsync_WhenSegmentIsGone_ReturnsMissing()
    {
        repository.DeleteAsync(Arg.Any<Guid>()).Returns((SessionSegment?)null);

        var result = await CreateCoordinator().DeleteAsync(Guid.NewGuid());

        Assert.IsType<SegmentChangeResult.SegmentMissing>(result);
        await segmentStore.DidNotReceive().PublishSegmentsAsync(Arg.Any<IReadOnlyCollection<SessionSegmentSnapshot>>(), Arg.Any<IReadOnlyCollection<Guid>>());
    }

    [Theory]
    [InlineData(SegmentWriteFailureReason.Create)]
    [InlineData(SegmentWriteFailureReason.Rename)]
    [InlineData(SegmentWriteFailureReason.Delete)]
    public async Task Write_WhenPersistenceFails_ReturnsReasonAndPreservesPublishedSegments(SegmentWriteFailureReason operation)
    {
        using var store = new SessionSegmentStore(new StorePublication(new InlineUiThreadDispatcher()));
        var existing = new SessionSegmentSnapshot(Guid.NewGuid(), session.Id, "Existing", 2, 5);
        await store.PublishSegmentsAsync([existing], []);
        var error = new IOException("storage unavailable");
        repository.InsertAsync(Arg.Any<SessionSegment>()).Returns(Task.FromException(error));
        repository.RenameAsync(existing.Id, Arg.Any<string>()).Returns(Task.FromException<SessionSegment?>(error));
        repository.DeleteAsync(existing.Id).Returns(Task.FromException<SessionSegment?>(error));
        var coordinator = CreateCoordinator(store);

        var result = operation switch
        {
            SegmentWriteFailureReason.Create => await coordinator.CreateAsync(session.Id, "New", 3, 6),
            SegmentWriteFailureReason.Rename => await coordinator.RenameAsync(existing.Id, "Renamed"),
            SegmentWriteFailureReason.Delete => await coordinator.DeleteAsync(existing.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var failure = Assert.IsType<SegmentChangeResult.Failed>(result);
        Assert.Equal(operation, failure.Reason);
        Assert.NotEmpty(failure.Details!);
        Assert.Equal([existing], store.GetForSession(session.Id));
    }

    [Fact]
    public async Task CreateAsync_WhenSessionIsGone_ReturnsMissingWithoutWriting()
    {
        var result = await CreateCoordinator().CreateAsync(Guid.NewGuid(), "Segment", 2, 5);

        Assert.IsType<SegmentChangeResult.SessionMissing>(result);
        await repository.DidNotReceive().InsertAsync(Arg.Any<SessionSegment>());
    }

    [Fact]
    public async Task CreateAsync_WithUnorderedRange_ReturnsInvalidWithoutWriting()
    {
        var result = await CreateCoordinator().CreateAsync(session.Id, "Segment", 5, 2);

        Assert.IsType<SegmentChangeResult.Invalid>(result);
        await repository.DidNotReceive().InsertAsync(Arg.Any<SessionSegment>());
    }

    [Fact]
    public void NextName_SkipsNamesInUse()
    {
        Assert.Equal("Segment 2", CreateCoordinator().NextName(session.Id));
    }

    [Fact]
    public async Task MatchAsync_WithFewerThanTwoTrackPointsInTheRange_ReturnsNoTrack()
    {
        AddSession(sourceId, [Point(1000), Point(1010)]);

        var result = await CreateCoordinator().MatchAsync(Source());

        Assert.IsType<SegmentMatchResult.NoTrack>(result);
        await matcher.DidNotReceiveWithAnyArgs().FindMatchesAsync(default!);
    }

    [Fact]
    public async Task MatchAsync_OrdersMatchesByMatchedLocalTime()
    {
        AddSession(sourceId, SourceTrack());
        var matchedId = AddMatchedSession();
        var late = TestSegmentMatches.Match(20, 22, matchedId);
        var early = TestSegmentMatches.Match(10, 12, matchedId);
        MatcherReturns(late, early);

        var result = await CreateCoordinator().MatchAsync(Source());

        var matched = Assert.IsType<SegmentMatchResult.Matched>(result);
        Assert.Equal([early, late], matched.Matches);
        Assert.Equal([early, late], matched.FirstPage.Comparisons.Select(comparison => comparison.Match));
    }

    [Fact]
    public async Task MatchAsync_DropsMatchesOfChangedSessions()
    {
        AddSession(sourceId, SourceTrack());
        var changedId = AddMatchedSession(TestSegmentMatches.Token with { ProcessedTelemetryRevision = 2 });
        var changed = TestSegmentMatches.Match(10, 12, changedId);
        MatcherReturns(changed);

        var result = await CreateCoordinator().MatchAsync(Source());

        var matched = Assert.IsType<SegmentMatchResult.Matched>(result);
        Assert.Empty(matched.Matches);
        Assert.Equal([changed], matched.FirstPage.UnavailableMatches);
    }

    [Fact]
    public async Task MatchAsync_WhenTheSessionStartsAnotherOperation_ReturnsCancelled()
    {
        AddSession(sourceId, SourceTrack());
        matcher.FindMatchesAsync(Arg.Any<SegmentMatchRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<SegmentMatchProgress>?>())
            .Returns(call => WaitForCancellationAsync(call.ArgAt<CancellationToken>(1)));

        var matching = CreateCoordinator().MatchAsync(Source());
        operationRunner.Start(sourceId, "other").Dispose();

        Assert.IsType<SegmentMatchResult.Cancelled>(await matching);
    }

    [Fact]
    public async Task LoadComparisonPageAsync_BuildsAtMostOnePage()
    {
        var matchedId = AddMatchedSession();
        var matches = Enumerable.Range(0, SegmentComparisonPage.Size + 2)
            .Select(index => TestSegmentMatches.Match(index, index + 2, matchedId))
            .ToArray();

        var page = await CreateCoordinator().LoadComparisonPageAsync(matches, 30, CancellationToken.None);

        Assert.Equal(matches.Take(SegmentComparisonPage.Size), page.Comparisons.Select(comparison => comparison.Match));
    }

    [Fact]
    public async Task LoadComparisonDetailAsync_WhenTheSessionChanged_ReturnsNull()
    {
        var matchedId = AddMatchedSession();
        var comparison = (await CreateCoordinator().LoadComparisonPageAsync(
            [TestSegmentMatches.Match(10, 12, matchedId)], 30, CancellationToken.None)).Comparisons.Single();
        contentReader.Sessions = [CatalogItem(matchedId, TestSegmentMatches.Token with { FullTrackPointsRevision = 2 })];

        Assert.Null(await CreateCoordinator().LoadComparisonDetailAsync(comparison, CancellationToken.None));
    }

    private SegmentMatchSource Source() =>
        new(sourceId, new TelemetryTimeRange(2, 5), new TrackTimeRange(1000, 30), DurationSeconds: 30);

    private static IReadOnlyList<TrackPoint> SourceTrack() =>
        Enumerable.Range(1000, 11).Select(time => Point(time)).ToArray();

    private static TrackPoint Point(double time) => new(time, time, 0, elevation: null);

    private void AddSession(Guid id, IReadOnlyList<TrackPoint> track, SessionContentToken? token = null)
    {
        contentReader.Sessions = [.. contentReader.Sessions, CatalogItem(id, token ?? TestSegmentMatches.Token)];
        contentReader.Tracks[id] = track;
    }

    private Guid AddMatchedSession(SessionContentToken? token = null)
    {
        var id = Guid.NewGuid();
        AddSession(id, SourceTrack(), token);
        contentReader.Telemetry[id] = TestSegmentMatches.Telemetry();
        return id;
    }

    private static SessionCatalogItem CatalogItem(Guid id, SessionContentToken token) =>
        new(id, "Session", 1000, 30, token);

    private void MatcherReturns(params SegmentMatch[] matches) =>
        matcher.FindMatchesAsync(Arg.Any<SegmentMatchRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<SegmentMatchProgress>?>())
            .Returns(matches);

    [Fact]
    public async Task FindStravaSegmentsAsync_WhenNotConnected_DoesNotFetch()
    {
        stravaService.GetConnectionSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new StravaConnectionSnapshot(null, null, IsConnected: false));

        var result = await CreateCoordinator().FindStravaSegmentsAsync(1_000, 10, CancellationToken.None);

        Assert.IsType<StravaSegmentFetchResult.NotConnected>(result);
        await stravaService.DidNotReceiveWithAnyArgs().FindSegmentsAsync(default, default, default);
    }

    [Fact]
    public async Task FindStravaSegmentsAsync_WithoutSessionTimestamp_ReturnsMissingSessionTiming()
    {
        stravaService.GetConnectionSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new StravaConnectionSnapshot("client", "secret", IsConnected: true));

        var result = await CreateCoordinator().FindStravaSegmentsAsync(null, 10, CancellationToken.None);

        Assert.IsType<StravaSegmentFetchResult.MissingSessionTiming>(result);
    }

    [Fact]
    public async Task FindStravaSegmentsAsync_WhenTheRequestThrows_ReturnsFailed()
    {
        stravaService.GetConnectionSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new StravaConnectionSnapshot("client", "secret", IsConnected: true));
        stravaService.FindSegmentsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StravaSegmentFetchResult>(new HttpRequestException("offline")));

        var result = await CreateCoordinator().FindStravaSegmentsAsync(1_000, 10, CancellationToken.None);

        Assert.Equal(StravaSegmentFetchFailureReason.Load, Assert.IsType<StravaSegmentFetchResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task ConnectStravaAsync_WhenTheRequestThrows_ReturnsFailed()
    {
        stravaService.ConnectAsync(Arg.Any<StravaConnectRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StravaConnectResult>(new HttpRequestException("offline")));

        var result = await CreateCoordinator().ConnectStravaAsync(
            new StravaConnectRequest("client", "secret", "http://localhost/exchange_token?code=abc"),
            CancellationToken.None);

        Assert.Equal(StravaConnectFailureReason.Connect, Assert.IsType<StravaConnectResult.Failed>(result).Reason);
    }

    private static async Task<IReadOnlyList<SegmentMatch>> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return [];
    }

    private SegmentCoordinator CreateCoordinator(ISessionSegmentStoreWriter? store = null) => new(repository, store ?? segmentStore, sessionStore, matcher, contentReader, operationRunner, new InlineBackgroundTaskRunner(), stravaService);
}
