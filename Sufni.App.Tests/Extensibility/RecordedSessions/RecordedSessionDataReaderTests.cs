using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.Extensibility.RecordedSessions;
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
    private readonly ITrackRepository trackRepository = Substitute.For<ITrackRepository>();
    private readonly TestSessionTelemetryProcessor telemetryProcessor = new();
    private readonly TestSessionProcessedTelemetryReader processedTelemetryReader = new();

    private RecordedSessionDataReader CreateReader() =>
        new(
            sessionRepository,
            sessionTrackReader,
            fullTrackPointReader,
            trackRepository,
            telemetryProcessor,
            processedTelemetryReader);

    [Fact]
    public void RecordedSessionCatalogItem_RequiresContentToken()
    {
        var id = Guid.NewGuid();
        var token = new RecordedSessionContentToken(1, 2, null, null, 1000, 20, 0);
        var item = new RecordedSessionCatalogItem(id, "Session", 1000, 20, token);

        var (actualId, name, timestamp, durationSeconds, actualToken) = item;

        Assert.Equal(id, actualId);
        Assert.Equal("Session", name);
        Assert.Equal(1000, timestamp);
        Assert.Equal(20, durationSeconds);
        Assert.Equal(token, actualToken);
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
                FullTrack = fullTrackId,
                DurationSeconds = 20,
                GpsOffsetSeconds = 1.5,
                Updated = 12,
                ProcessedTelemetryRevision = 11,
                TrackProjectionRevision = 12,
            },
            new Session(unlinkedSessionId, "Unlinked", "", setup: null, timestamp: 2000)
            {
                Updated = 34,
                TrackProjectionRevision = 34,
            },
        ]);
        trackRepository.GetTrackPayloadMetadataByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns([new TrackPayloadMetadata(fullTrackId, PointsRevision: 56)]);

        var result = await CreateReader().GetSessionsAsync(TestContext.Current.CancellationToken);

        var linked = Assert.Single(result, item => item.Id == linkedSessionId);
        Assert.Equal(new RecordedSessionContentToken(
            ProcessedTelemetryRevision: 11,
            TrackProjectionRevision: 12,
            FullTrackId: fullTrackId,
            FullTrackPointsRevision: 56,
            Timestamp: 1000,
            DurationSeconds: 20,
            GpsOffsetSeconds: 1.5), linked.ContentToken);

        var unlinked = Assert.Single(result, item => item.Id == unlinkedSessionId);
        Assert.Equal(new RecordedSessionContentToken(
            ProcessedTelemetryRevision: 0,
            TrackProjectionRevision: 34,
            FullTrackId: null,
            FullTrackPointsRevision: null,
            Timestamp: 2000,
            DurationSeconds: null,
            GpsOffsetSeconds: 0), unlinked.ContentToken);

        await trackRepository.Received(1).GetTrackPayloadMetadataByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(fullTrackId)));
        await trackRepository.DidNotReceive().GetTrackPayloadAsync(Arg.Any<Guid>(), Arg.Any<long>());
        await fullTrackPointReader.DidNotReceive().GetTrackPointsAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
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
            FullTrack = fullTrackId,
            Updated = 12,
            TrackProjectionRevision = 12,
        });
        trackRepository.GetTrackPayloadMetadataAsync(fullTrackId)
            .Returns(new TrackPayloadMetadata(fullTrackId, PointsRevision: 56));

        var result = await CreateReader().GetSessionAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(new RecordedSessionContentToken(
            ProcessedTelemetryRevision: 0,
            TrackProjectionRevision: 12,
            FullTrackId: fullTrackId,
            FullTrackPointsRevision: 56,
            Timestamp: 1000,
            DurationSeconds: null,
            GpsOffsetSeconds: 0), result.ContentToken);
        await trackRepository.DidNotReceive().GetTrackPayloadAsync(Arg.Any<Guid>(), Arg.Any<long>());
    }

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
    public async Task GetExactSnapshotAsync_LoadsOnlySelectedFamilyAtExpectedRevision()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            DurationSeconds = 20,
            GpsOffsetSeconds = 1.5,
            ProcessedTelemetryRevision = 11,
            TrackProjectionRevision = 12,
        };
        var token = CreateToken(session);
        var telemetry = TestTelemetryData.CreateProcessed();
        processedTelemetryReader.Set(sessionId, telemetry);
        sessionRepository.GetSessionAsync(sessionId).Returns(session);

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            token,
            RecordedSessionContentSelection.ProcessedTelemetry,
            TestContext.Current.CancellationToken);

        var snapshot = Assert.IsType<RecordedSessionContentSnapshotResult.Available>(result).Snapshot;
        Assert.Equal(token, snapshot.Token);
        Assert.Equal(RecordedSessionContentSelection.ProcessedTelemetry, snapshot.Selection);
        Assert.Same(telemetry, snapshot.ProcessedTelemetry);
        Assert.Null(snapshot.Track);
        Assert.Equal([(sessionId, 11L)], processedTelemetryReader.ExactReads);
        await sessionTrackReader.DidNotReceive().GetSessionTrackExactAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExactSnapshotAsync_ReturnsStaleWithoutPayloadLoad_WhenTokenIsStale()
    {
        var sessionId = Guid.NewGuid();
        var current = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            ProcessedTelemetryRevision = 12,
            TrackProjectionRevision = 20,
        };
        sessionRepository.GetSessionAsync(sessionId).Returns(current);
        var stale = CreateToken(current) with { ProcessedTelemetryRevision = 11 };

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            stale,
            RecordedSessionContentSelection.All,
            TestContext.Current.CancellationToken);

        Assert.IsType<RecordedSessionContentSnapshotResult.Stale>(result);
        Assert.Empty(processedTelemetryReader.ExactReads);
        await sessionTrackReader.DidNotReceive().GetSessionTrackExactAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExactSnapshotAsync_ReturnsMissing_WhenSessionDoesNotExist()
    {
        var sessionId = Guid.NewGuid();
        var token = new RecordedSessionContentToken(1, 1, null, null, 1000, 20, 0);
        sessionRepository.GetSessionAsync(sessionId).Returns((Session?)null);

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            token,
            RecordedSessionContentSelection.All,
            TestContext.Current.CancellationToken);

        Assert.IsType<RecordedSessionContentSnapshotResult.Missing>(result);
        Assert.Empty(processedTelemetryReader.ExactReads);
        await sessionTrackReader.DidNotReceive().GetSessionTrackExactAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
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
            TrackProjectionRevision = 12,
        };
        sessionRepository.GetSessionAsync(sessionId).Returns(session);

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            CreateToken(session),
            RecordedSessionContentSelection.ProcessedTelemetry,
            TestContext.Current.CancellationToken);

        Assert.IsType<RecordedSessionContentSnapshotResult.Missing>(result);
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
            TrackProjectionRevision = 12,
        };
        var changed = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            DurationSeconds = 21,
            ProcessedTelemetryRevision = 11,
            TrackProjectionRevision = 13,
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
            RecordedSessionContentSelection.ProcessedTelemetry,
            TestContext.Current.CancellationToken);
        await payloadRequested.Task;
        releasePayload.SetResult(TestTelemetryData.CreateProcessed());

        var result = await read;

        Assert.IsType<RecordedSessionContentSnapshotResult.Stale>(result);
        Assert.Equal([(sessionId, 11L)], processedTelemetryReader.ExactReads);
    }

    [Fact]
    public async Task GetExactSnapshotAsync_UsesExactTrackProjectionWithoutLatestRetry()
    {
        var sessionId = Guid.NewGuid();
        var session = new Session(sessionId, "Session", "", setup: null, timestamp: 1000)
        {
            TrackProjectionRevision = 12,
        };
        var track = new List<TrackPoint>
        {
            new(1000, 1, 1, 100),
            new(1001, 2, 2, 110),
        };
        sessionRepository.GetSessionAsync(sessionId).Returns(session);
        sessionTrackReader.GetSessionTrackExactAsync(sessionId, 12, Arg.Any<CancellationToken>())
            .Returns(track);

        var result = await CreateReader().GetExactSnapshotAsync(
            sessionId,
            CreateToken(session),
            RecordedSessionContentSelection.Track,
            TestContext.Current.CancellationToken);

        var snapshot = Assert.IsType<RecordedSessionContentSnapshotResult.Available>(result).Snapshot;
        Assert.Same(track, snapshot.Track);
        await sessionTrackReader.Received(1).GetSessionTrackExactAsync(
            sessionId,
            12,
            Arg.Any<CancellationToken>());
        await sessionTrackReader.DidNotReceive().GetSessionTrackAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
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
            TrackProjectionRevision = 12,
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
            TrackProjectionRevision = 12,
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
            TrackProjectionRevision = 12,
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
            TrackProjectionRevision = 12,
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

    private static RecordedSessionContentToken CreateToken(
        Session session,
        long? fullTrackPointsRevision = null) =>
        new(
            session.ProcessedTelemetryRevision,
            session.TrackProjectionRevision,
            session.FullTrack,
            fullTrackPointsRevision,
            session.Timestamp,
            session.DurationSeconds,
            session.GpsOffsetSeconds);
}
