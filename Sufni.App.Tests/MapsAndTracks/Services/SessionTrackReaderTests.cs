using DynamicData;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.MapsAndTracks.Services;
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
        sessionRepository.GetSessionTrackAsync(sessionId).Returns(first, second);
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        var sessionStore = CreateSessionStore(source);
        using var reader = new SessionTrackReader(sessionRepository, sessionStore, capacity: 8);

        var firstRead = await reader.GetSessionTrackAsync(sessionId, sessionUpdated: 10);
        var cachedRead = await reader.GetSessionTrackAsync(sessionId, sessionUpdated: 10);
        var updatedRead = await reader.GetSessionTrackAsync(sessionId, sessionUpdated: 11);

        Assert.Same(first, firstRead);
        Assert.Same(first, cachedRead);
        Assert.Same(second, updatedRead);
        await sessionRepository.Received(2).GetSessionTrackAsync(sessionId);
    }

    [Fact]
    public async Task StoreChanges_EvictOnlyWhenTrackRelevantFieldsChange()
    {
        var sessionId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var first = Points(1);
        var second = Points(2);
        var sessionRepository = Substitute.For<ISessionRepository>();
        sessionRepository.GetSessionTrackAsync(sessionId).Returns(first, second);
        using var source = new SourceCache<SessionSnapshot, Guid>(snapshot => snapshot.Id);
        var sessionStore = CreateSessionStore(source);
        using var reader = new SessionTrackReader(sessionRepository, sessionStore, capacity: 8);
        source.AddOrUpdate(Snapshot(sessionId, updated: 10, fullTrackId: trackId, gpsOffsetSeconds: 0, name: "session"));

        var firstRead = await reader.GetSessionTrackAsync(sessionId, sessionUpdated: 10);
        source.AddOrUpdate(Snapshot(sessionId, updated: 10, fullTrackId: trackId, gpsOffsetSeconds: 0, name: "renamed"));
        var unchangedRead = await reader.GetSessionTrackAsync(sessionId, sessionUpdated: 10);
        source.AddOrUpdate(Snapshot(sessionId, updated: 11, fullTrackId: trackId, gpsOffsetSeconds: 0, name: "renamed"));
        var evictedRead = await reader.GetSessionTrackAsync(sessionId, sessionUpdated: 10);

        Assert.Same(first, firstRead);
        Assert.Same(first, unchangedRead);
        Assert.Same(second, evictedRead);
        await sessionRepository.Received(2).GetSessionTrackAsync(sessionId);
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
        gpsOffsetSeconds);
}
