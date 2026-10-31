using Sufni.App.MapsAndTracks.Stores;
using NSubstitute;
using Sufni.App.MapsAndTracks.Models;

using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Services;

public class SessionContentReaderTests
{
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTrackReader sessionTrackReader = Substitute.For<ISessionTrackReader>();
    private readonly ITrackStore trackStore = Substitute.For<ITrackStore>();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();

    private SessionContentReader CreateReader() =>
        new(
            sessionRepository,
            sessionTrackReader,
            trackStore,
            processedTelemetryReader);

    [Fact]
    public async Task GetExactSnapshotAsync_RejectsAChangedFractionalWindowOrigin()
    {
        var session = new Session(Guid.NewGuid(), "ride", "", null, 100) { DurationSeconds = 2, WindowStartSeconds = 10.25 };
        sessionRepository.GetSessionAsync(session.Id).Returns(session);
        trackStore.GetAll().Returns([new TrackSnapshot(Guid.NewGuid(), 100, 105, 1)]);
        var reader = CreateReader();
        var token = (await reader.GetSessionAsync(session.Id))!.ContentToken;
        Assert.Equal(0.25, token.TrackKey!.Value.WindowOriginFraction);
        session.WindowStartSeconds = 10.5;

        var result = await reader.GetExactSnapshotAsync(session.Id, token, SessionContentSelection.Track, TestContext.Current.CancellationToken);

        Assert.IsType<SessionContentSnapshotResult.Stale>(result);
        await sessionTrackReader.DidNotReceiveWithAnyArgs().GetSessionTrackAsync(default, default);
    }

    [Fact]
    public async Task GetSessionsAsync_ProvidesContentTokensWithoutLoadingPayloads()
    {
        var linkedSessionId = Guid.NewGuid();
        var unlinkedSessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        sessionRepository.GetSessionsAsync().Returns([
            new Session(linkedSessionId, "Linked", "", setup: null, timestamp: 1000)
            {
                DurationSeconds = 20,
                GpsOffsetSeconds = 1.5,
                Updated = 12,
                ProcessedTelemetryRevision = 11,

            },
            new Session(unlinkedSessionId, "Unlinked", "", setup: null, timestamp: 2000)
            {
                Updated = 34,

            },
        ]);
        trackStore.GetAll().Returns([new TrackSnapshot(fullTrackId, 1000, 1030, 56)]);

        var result = await CreateReader().GetSessionsAsync(TestContext.Current.CancellationToken);

        var linked = Assert.Single(result, item => item.Id == linkedSessionId);
        Assert.Equal(new SessionContentToken(
            ProcessedTelemetryRevision: 11,

            FullTrackId: fullTrackId,
            FullTrackPointsRevision: 56,
            Timestamp: 1000,
            DurationSeconds: 20,
            GpsOffsetSeconds: 1.5), linked.ContentToken);

        var unlinked = Assert.Single(result, item => item.Id == unlinkedSessionId);
        Assert.Equal(new SessionContentToken(
            ProcessedTelemetryRevision: 0,

            FullTrackId: null,
            FullTrackPointsRevision: null,
            Timestamp: 2000,
            DurationSeconds: null,
            GpsOffsetSeconds: 0), unlinked.ContentToken);

        await sessionTrackReader.DidNotReceive().GetSessionTrackAsync(Arg.Any<SessionTrackKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSessionAsync_IncludesLinkedTrackUpdateInContentVersion()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(
            sessionId,
            "Session",
            "",
            setup: null,
            timestamp: 1000)
        {
            Updated = 12,
            DurationSeconds = 20,

        });
        trackStore.GetAll().Returns([new TrackSnapshot(fullTrackId, 1000, 1030, 56)]);

        var result = await CreateReader().GetSessionAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(new SessionContentToken(
            ProcessedTelemetryRevision: 0,

            FullTrackId: fullTrackId,
            FullTrackPointsRevision: 56,
            Timestamp: 1000,
            DurationSeconds: 20,
            GpsOffsetSeconds: 0), result.ContentToken);
    }

    [Fact]
    public async Task GetExactSnapshotAsync_LoadsOnlySelectedFamilyAtExpectedRevision()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            DurationSeconds = 20,
            GpsOffsetSeconds = 1.5,
            ProcessedTelemetryRevision = 11,

        };
        var token = CreateToken(session);
        var telemetry = TestTelemetryData.CreateProcessed();
        processedTelemetryReader.Set(sessionId, telemetry);
        sessionRepository.GetSessionAsync(sessionId).Returns(session);

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            token,
            SessionContentSelection.ProcessedTelemetry,
            TestContext.Current.CancellationToken);

        var snapshot = Assert.IsType<SessionContentSnapshotResult.Available>(result).Snapshot;
        Assert.Equal(token, snapshot.Token);
        Assert.Equal(SessionContentSelection.ProcessedTelemetry, snapshot.Selection);
        Assert.Same(telemetry, snapshot.ProcessedTelemetry);
        Assert.Null(snapshot.Track);
        Assert.Equal([(sessionId, 11L)], processedTelemetryReader.ExactReads);
        await sessionTrackReader.DidNotReceive().GetSessionTrackAsync(
            Arg.Any<SessionTrackKey>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExactSnapshotAsync_ReturnsStaleWithoutPayloadLoad_WhenTokenIsStale()
    {
        var sessionId = Guid.NewGuid();
        var current = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            ProcessedTelemetryRevision = 12,

        };
        sessionRepository.GetSessionAsync(sessionId).Returns(current);
        var stale = CreateToken(current) with { ProcessedTelemetryRevision = 11 };

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            stale,
            SessionContentSelection.All,
            TestContext.Current.CancellationToken);

        Assert.IsType<SessionContentSnapshotResult.Stale>(result);
        Assert.Empty(processedTelemetryReader.ExactReads);
        await sessionTrackReader.DidNotReceive().GetSessionTrackAsync(
            Arg.Any<SessionTrackKey>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExactSnapshotAsync_ReturnsMissing_WhenSessionDoesNotExist()
    {
        var sessionId = Guid.NewGuid();
        var token = new SessionContentToken(1, null, null, 1000, 20, 0);
        sessionRepository.GetSessionAsync(sessionId).Returns((Session?)null);

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            token,
            SessionContentSelection.All,
            TestContext.Current.CancellationToken);

        Assert.IsType<SessionContentSnapshotResult.Missing>(result);
        Assert.Empty(processedTelemetryReader.ExactReads);
        await sessionTrackReader.DidNotReceive().GetSessionTrackAsync(
            Arg.Any<SessionTrackKey>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExactSnapshotAsync_ReturnsMissing_WhenSelectedPayloadIsAbsentAtCurrentToken()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            DurationSeconds = 20,
            ProcessedTelemetryRevision = 11,

        };
        sessionRepository.GetSessionAsync(sessionId).Returns(session);

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            CreateToken(session),
            SessionContentSelection.ProcessedTelemetry,
            TestContext.Current.CancellationToken);

        Assert.IsType<SessionContentSnapshotResult.Missing>(result);
        Assert.Equal([(sessionId, 11L)], processedTelemetryReader.ExactReads);
    }

    [Fact]
    public async Task GetExactSnapshotAsync_ReturnsStale_WhenMetadataChangesDuringSelectedPayloadLoad()
    {
        var sessionId = Guid.NewGuid();
        var initial = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            DurationSeconds = 20,
            ProcessedTelemetryRevision = 11,

        };
        var changed = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            DurationSeconds = 21,
            ProcessedTelemetryRevision = 11,

        };
        sessionRepository.GetSessionAsync(sessionId).Returns(initial, changed);
        var payloadRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePayload = new TaskCompletionSource<Sufni.Telemetry.TelemetryData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        processedTelemetryReader.ExactReader = (_, _, _) =>
        {
            payloadRequested.TrySetResult();
            return releasePayload.Task;
        };

        var read = CreateReader().GetExactSnapshotAsync(
            sessionId,
            CreateToken(initial),
            SessionContentSelection.ProcessedTelemetry,
            TestContext.Current.CancellationToken);
        await payloadRequested.Task;
        releasePayload.SetResult(TestTelemetryData.CreateProcessed());

        var result = await read;

        Assert.IsType<SessionContentSnapshotResult.Stale>(result);
        Assert.Equal([(sessionId, 11L)], processedTelemetryReader.ExactReads);
    }

    [Fact]
    public async Task GetExactSnapshotAsync_UsesSelectedTrackInputs()
    {
        var trackId = Guid.NewGuid();
        var session = new Session(Guid.NewGuid(), "ride", "", null, 1000)
        { DurationSeconds = 2, GpsOffsetSeconds = 0.5 };
        sessionRepository.GetSessionAsync(session.Id).Returns(session);
        trackStore.GetAll().Returns([new TrackSnapshot(trackId, 1000, 1003, 7)]);
        var token = new SessionContentToken(0, trackId, 7, 1000, 2, 0.5);
        IReadOnlyList<TrackPoint> points = [new(1000.5, 1, 2, 3), new(1001.5, 2, 3, 4)];
        sessionTrackReader.GetSessionTrackAsync(token.TrackKey!.Value, Arg.Any<CancellationToken>()).Returns(points);

        var result = await CreateReader().GetExactSnapshotAsync(session.Id, token,
            SessionContentSelection.Track, TestContext.Current.CancellationToken);

        Assert.Same(points, Assert.IsType<SessionContentSnapshotResult.Available>(result).Snapshot.Track);
    }

    [Fact]
    public async Task GetExactSnapshotAsync_RejectsResultWhenAFullerTrackArrivesDuringRead()
    {
        var session = new Session(Guid.NewGuid(), "ride", "", null, 1000) { DurationSeconds = 20 };
        sessionRepository.GetSessionAsync(session.Id).Returns(session);
        var initial = new TrackSnapshot(Guid.NewGuid(), 1000, 1010, 1);
        trackStore.GetAll().Returns([initial]);
        var reader = CreateReader();
        var token = (await reader.GetSessionAsync(session.Id))!.ContentToken;
        var completion = new TaskCompletionSource<IReadOnlyList<TrackPoint>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionTrackReader.GetSessionTrackAsync(token.TrackKey!.Value, Arg.Any<CancellationToken>()).Returns(completion.Task);

        var pending = reader.GetExactSnapshotAsync(session.Id, token, SessionContentSelection.Track, TestContext.Current.CancellationToken);
        trackStore.GetAll().Returns([initial, new TrackSnapshot(Guid.NewGuid(), 1000, 1020, 1)]);
        completion.SetResult([new TrackPoint(1000, 1, 2, 3), new TrackPoint(1001, 2, 3, 4)]);

        Assert.IsType<SessionContentSnapshotResult.Stale>(await pending);
    }

    private static SessionContentToken CreateToken(
        Session session,
        long? fullTrackPointsRevision = null) =>
        new(
            session.ProcessedTelemetryRevision,
            null,
            fullTrackPointsRevision,
            session.Timestamp,
            session.DurationSeconds,
            session.GpsOffsetSeconds);
}
