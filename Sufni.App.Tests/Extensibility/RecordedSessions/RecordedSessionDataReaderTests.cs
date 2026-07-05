using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Extensibility.RecordedSessions;

public class RecordedSessionDataReaderTests
{
    private readonly ISessionRepository sessionRepository = Substitute.For<ISessionRepository>();
    private readonly ISessionTrackReader sessionTrackReader = Substitute.For<ISessionTrackReader>();
    private readonly IFullTrackPointReader fullTrackPointReader = Substitute.For<IFullTrackPointReader>();
    private readonly TestSessionTelemetryProcessor telemetryProcessor = new();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();

    private RecordedSessionDataReader CreateReader() =>
        new(sessionRepository, sessionTrackReader, fullTrackPointReader, telemetryProcessor, processedTelemetryReader);

    [Fact]
    public async Task GetProcessedTelemetryAsync_UsesProcessedTelemetryReader()
    {
        var sessionId = Guid.NewGuid();
        var telemetry = TestTelemetryData.CreateProcessed();
        processedTelemetryReader.Set(sessionId, telemetry);

        var result = await CreateReader().GetProcessedTelemetryAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Same(telemetry, result);
        Assert.Equal(1, processedTelemetryReader.GetCallCount(sessionId));
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(Arg.Any<Guid>());
    }

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
            Updated = 12,
        });
        sessionTrackReader.GetSessionTrackAsync(sessionId, 12, Arg.Any<CancellationToken>()).Returns(cachedTrack);

        var result = await CreateReader().GetTrackAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Same(cachedTrack, result);
        await fullTrackPointReader.DidNotReceive().GetTrackPointsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await sessionRepository.DidNotReceive().GetSessionTrackAsync(Arg.Any<Guid>());
        // Alignment is decided from the session row alone; the heavy processed
        // telemetry blob must never be loaded while enumerating candidates.
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task GetTrackAsync_RegeneratesSessionTrack_WhenCachedTrackIsMissing()
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
            FullTrack = fullTrackId,
            DurationSeconds = 2.0,
            GpsOffsetSeconds = 0.5,
            HasProcessedData = true,
            Updated = 12,
        });
        var fullTrackPoints = new List<TrackPoint>
        {
            new(1000.5, 1, 1, 100),
            new(1001.5, 2, 2, 110),
            new(1002.5, 3, 3, 120),
        };
        sessionTrackReader.GetSessionTrackAsync(sessionId, 12, Arg.Any<CancellationToken>())
            .Returns((List<TrackPoint>?)null);
        fullTrackPointReader.GetTrackPointsAsync(fullTrackId, Arg.Any<CancellationToken>()).Returns(fullTrackPoints);

        var result = await CreateReader().GetTrackAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Count >= 2);
        Assert.Equal(1000.5, result[0].Time, precision: 6);
        Assert.Equal(1002.5, result[^1].Time, precision: 6);
        await fullTrackPointReader.Received(1).GetTrackPointsAsync(fullTrackId, Arg.Any<CancellationToken>());
        await sessionRepository.DidNotReceive().GetSessionTrackAsync(Arg.Any<Guid>());
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task GetTrackAsync_RegeneratesSessionTrack_WhenGpsSamplesDoNotCoverExactTelemetryWindow()
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
            FullTrack = fullTrackId,
            DurationSeconds = 2.75,
            GpsOffsetSeconds = 0.1,
            HasProcessedData = true,
            Updated = 12,
        });
        var fullTrackPoints = new List<TrackPoint>
        {
            new(1000.2, 1, 1, 100),
            new(1001.5, 2, 2, 110),
            new(1002.85, 3, 3, 120),
        };
        sessionTrackReader.GetSessionTrackAsync(sessionId, 12, Arg.Any<CancellationToken>())
            .Returns((List<TrackPoint>?)null);
        fullTrackPointReader.GetTrackPointsAsync(fullTrackId, Arg.Any<CancellationToken>()).Returns(fullTrackPoints);

        var result = await CreateReader().GetTrackAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Count >= 2);
        Assert.Equal(1000.1, result[0].Time, precision: 6);
        Assert.Equal(1002.85, result[^1].Time, precision: 6);
        await fullTrackPointReader.Received(1).GetTrackPointsAsync(fullTrackId, Arg.Any<CancellationToken>());
        await sessionRepository.DidNotReceive().GetSessionTrackAsync(Arg.Any<Guid>());
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
            Updated = 12,
        });
        var fullTrackPoints = new List<TrackPoint>
        {
            new(1000.5, 1, 1, 100),
            new(1001.5, 2, 2, 110),
            new(1002.5, 3, 3, 120),
        };
        sessionTrackReader.GetSessionTrackAsync(sessionId, 12, Arg.Any<CancellationToken>()).Returns(staleTrack);
        fullTrackPointReader.GetTrackPointsAsync(fullTrackId, Arg.Any<CancellationToken>()).Returns(fullTrackPoints);

        var result = await CreateReader().GetTrackAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotSame(staleTrack, result);
        Assert.NotNull(result);
        Assert.Equal(1000.5, result[0].Time, precision: 6);
        Assert.Equal(1002.5, result[^1].Time, precision: 6);
        await fullTrackPointReader.Received(1).GetTrackPointsAsync(fullTrackId, Arg.Any<CancellationToken>());
        await sessionRepository.DidNotReceive().GetSessionTrackAsync(Arg.Any<Guid>());
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(Arg.Any<Guid>());
    }
}
