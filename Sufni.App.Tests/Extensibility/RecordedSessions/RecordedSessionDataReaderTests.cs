using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Extensibility.RecordedSessions;

public class RecordedSessionDataReaderTests
{
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISynchronizableRepository<Track> trackEntityRepository = Substitute.For<ISynchronizableRepository<Track>>();
    private readonly TestSessionTelemetryProcessor telemetryProcessor = new();

    private RecordedSessionDataReader CreateReader() =>
        new(sessionRepository, trackEntityRepository, telemetryProcessor);

    [Fact]
    public async Task GetTrackAsync_ReturnsCachedTrack_WhenAlignedWithSessionTimestamp()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var cachedTrack = new List<TrackPoint>
        {
            new(1001.5, 1, 1, 100),
            new(1002.5, 2, 2, 110),
        };
        sessionRepository.GetSessionTrackAsync(sessionId).Returns(cachedTrack);
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(
            sessionId,
            "Session",
            "",
            setup: null,
            timestamp: 1000)
        {
            FullTrack = fullTrackId,
            DurationSeconds = 3.0,
            GpsOffsetSeconds = 1.5,
            HasProcessedData = true,
        });

        var result = await CreateReader().GetTrackAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Same(cachedTrack, result);
        await trackEntityRepository.DidNotReceive().GetAsync(Arg.Any<Guid>());
        // Alignment is decided from the session row alone; the heavy processed
        // telemetry blob must never be loaded while enumerating candidates.
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task GetTrackAsync_RegeneratesSessionTrack_WhenCachedTrackIsMissing()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        sessionRepository.GetSessionTrackAsync(sessionId).Returns((List<TrackPoint>?)null);
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(
            sessionId,
            "Session",
            "",
            setup: null,
            timestamp: 1000)
        {
            FullTrack = fullTrackId,
            DurationSeconds = 2.0,
            GpsOffsetSeconds = 0.5,
            HasProcessedData = true,
        });
        trackEntityRepository.GetAsync(fullTrackId).Returns(new Track
        {
            Id = fullTrackId,
            Points =
            [
                new TrackPoint(1000.5, 1, 1, 100),
                new TrackPoint(1001.5, 2, 2, 110),
                new TrackPoint(1002.5, 3, 3, 120),
            ],
        });

        var result = await CreateReader().GetTrackAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Count >= 2);
        Assert.Equal(1000.5, result[0].Time, precision: 6);
        Assert.Equal(1002.5, result[^1].Time, precision: 6);
        await trackEntityRepository.Received(1).GetAsync(fullTrackId);
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task GetTrackAsync_RegeneratesSessionTrack_WhenCachedTrackUsesOldOffset()
    {
        var sessionId = Guid.NewGuid();
        var fullTrackId = Guid.NewGuid();
        var staleTrack = new List<TrackPoint>
        {
            new(1000.0, 1, 1, 100),
            new(1001.0, 2, 2, 110),
        };
        sessionRepository.GetSessionTrackAsync(sessionId).Returns(staleTrack);
        sessionRepository.GetSessionAsync(sessionId).Returns(new Session(
            sessionId,
            "Session",
            "",
            setup: null,
            timestamp: 1000)
        {
            FullTrack = fullTrackId,
            DurationSeconds = 2.0,
            GpsOffsetSeconds = 0.5,
            HasProcessedData = true,
        });
        trackEntityRepository.GetAsync(fullTrackId).Returns(new Track
        {
            Id = fullTrackId,
            Points =
            [
                new TrackPoint(1000.5, 1, 1, 100),
                new TrackPoint(1001.5, 2, 2, 110),
                new TrackPoint(1002.5, 3, 3, 120),
            ],
        });

        var result = await CreateReader().GetTrackAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotSame(staleTrack, result);
        Assert.NotNull(result);
        Assert.Equal(1000.5, result[0].Time, precision: 6);
        Assert.Equal(1002.5, result[^1].Time, precision: 6);
        await trackEntityRepository.Received(1).GetAsync(fullTrackId);
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(Arg.Any<Guid>());
    }
}
