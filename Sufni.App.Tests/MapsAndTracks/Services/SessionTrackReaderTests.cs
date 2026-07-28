using DynamicData;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class SessionTrackReaderTests
{
    [Fact]
    public async Task GetSessionTrackAsync_CachesBySessionAndUpdatedTimestamp()
    {
        var sessionId = Guid.NewGuid();
        var first = Points(1);
        var second = Points(2);
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetSessionTrackAsync(sessionId, Arg.Any<long>()).Returns(first, second);
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        var sessionStore = CreateSessionStore(source);
        using var reader = new SessionTrackReader(sessionRepository, sessionStore, capacity: 8);

        var firstRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);
        var cachedRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);
        var updatedRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 11, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(first, firstRead);
        Assert.Same(first, cachedRead);
        Assert.Same(second, updatedRead);
        await sessionRepository.Received(2).GetSessionTrackAsync(sessionId, Arg.Any<long>());
    }

    [Fact]
    public async Task GetSessionTrackAsync_EvictsByPointWeight()
    {
        var sessionId = Guid.NewGuid();
        var first = Points(1);
        var second = Points(2);
        var reloaded = Points(3);
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetSessionTrackAsync(sessionId, Arg.Any<long>()).Returns(first, second, reloaded);
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        var sessionStore = CreateSessionStore(source);
        using var reader = new SessionTrackReader(sessionRepository, sessionStore, capacity: 8, pointBudget: 3);

        var firstRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);
        _ = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 11, cancellationToken: TestContext.Current.CancellationToken);
        var repeatedRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(first, firstRead);
        Assert.Same(reloaded, repeatedRead);
        await sessionRepository.Received(3).GetSessionTrackAsync(sessionId, Arg.Any<long>());
    }

    [Fact]
    public async Task GetSessionTrackAsync_RetriesOnce_WhenProjectionRevisionChangesDuringRead()
    {
        var sessionId = Guid.NewGuid();
        var points = Points(2);
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetSessionTrackAsync(sessionId, 10).Returns((List<TrackPoint>?)null);
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(sessionId, "session", string.Empty, null)
        {
            TrackProjectionRevision = 11,
        });
        sessionRepository.GetSessionTrackAsync(sessionId, 11).Returns(points);
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        using var reader = new SessionTrackReader(sessionRepository, CreateSessionStore(source), capacity: 8);

        var result = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(points, result);
        await sessionRepository.Received(1).GetSessionTrackAsync(sessionId, 10);
        await sessionRepository.Received(1).GetSessionTrackAsync(sessionId, 11);
    }

    [Fact]
    public async Task GetSessionTrackExactAsync_DoesNotRetryToNewerProjection()
    {
        var sessionId = Guid.NewGuid();
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetSessionTrackAsync(sessionId, 10).Returns((List<TrackPoint>?)null);
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(sessionId, "session", string.Empty, null)
        {
            TrackProjectionRevision = 11,
        });
        sessionRepository.GetSessionTrackAsync(sessionId, 11).Returns(Points(2));
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        using var reader = new SessionTrackReader(sessionRepository, CreateSessionStore(source), capacity: 8);

        var result = await reader.GetSessionTrackExactAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await sessionRepository.Received(1).GetSessionTrackAsync(sessionId, 10);
        await sessionRepository.DidNotReceive().GetSessionTrackAsync(sessionId, 11);
        await sessionRepository.DidNotReceive().GetSessionAsync(sessionId);
    }

    [Fact]
    public async Task GetSessionTrackExactAsync_ReusesUnchangedRevision()
    {
        var sessionId = Guid.NewGuid();
        var points = Points(2);
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetSessionTrackAsync(sessionId, 10).Returns(points);
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        using var reader = new SessionTrackReader(sessionRepository, CreateSessionStore(source), capacity: 8);

        var first = await reader.GetSessionTrackExactAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetSessionTrackExactAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(points, first);
        Assert.Same(points, second);
        await sessionRepository.Received(1).GetSessionTrackAsync(sessionId, 10);
    }

    [Fact]
    public async Task StoreChanges_EvictOnlyWhenTrackRelevantFieldsChange()
    {
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var first = Points(1);
        var second = Points(2);
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetSessionTrackAsync(sessionId, Arg.Any<long>()).Returns(first, second);
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        var sessionStore = CreateSessionStore(source);
        using var reader = new SessionTrackReader(sessionRepository, sessionStore, capacity: 8);
        source.AddOrUpdate(Snapshot(sessionId, updated: 10, trackProjectionRevision: 10, fullTrackId: trackId, gpsOffsetSeconds: 0, name: "session"));

        var firstRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);
        source.AddOrUpdate(Snapshot(sessionId, updated: 11, trackProjectionRevision: 10, fullTrackId: trackId, gpsOffsetSeconds: 0, name: "renamed"));
        var unchangedRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);
        source.AddOrUpdate(Snapshot(sessionId, updated: 11, trackProjectionRevision: 11, fullTrackId: trackId, gpsOffsetSeconds: 0, name: "renamed"));
        var evictedRead = await reader.GetSessionTrackAsync(sessionId, trackProjectionRevision: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(first, firstRead);
        Assert.Same(first, unchangedRead);
        Assert.Same(second, evictedRead);
        await sessionRepository.Received(2).GetSessionTrackAsync(sessionId, Arg.Any<long>());
    }

    private static ISessionStore CreateSessionStore(SourceCache<SessionSnapshot, Guid> source)
    {
        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.Connect().Returns(source.Connect());
        return sessionStore;
    }

    private static List<TrackPoint> Points(double startTime) =>
    [
        new TrackPoint(startTime, 1, 1, 10),
        new TrackPoint(startTime + 1, 2, 2, 11),
    ];

    private static SessionSnapshot Snapshot(
        Guid id,
        long updated,
        long trackProjectionRevision,
        Guid? fullTrackId,
        double gpsOffsetSeconds,
        string name) => new(
        id,
        name,
        "desc",
        SetupId: null,
        Timestamp: 100,
        fullTrackId,
        HasProcessedData: true,
        ProcessingFingerprintJson: null,
        FrontSpringRate: null,
        FrontHighSpeedCompression: null,
        FrontLowSpeedCompression: null,
        FrontLowSpeedRebound: null,
        FrontHighSpeedRebound: null,
        RearSpringRate: null,
        RearHighSpeedCompression: null,
        RearLowSpeedCompression: null,
        RearLowSpeedRebound: null,
        RearHighSpeedRebound: null,
        updated,
        DurationSeconds: 10,
        DistanceMeters: 1,
        AscentMeters: 0,
        DescentMeters: 0,
        gpsOffsetSeconds,
        TrackProjectionRevision: trackProjectionRevision);
}
