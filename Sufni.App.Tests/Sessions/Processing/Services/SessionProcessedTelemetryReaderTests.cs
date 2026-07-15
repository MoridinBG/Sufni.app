using NSubstitute;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Processing.Services;

public class SessionProcessedTelemetryReaderTests
{
    [Fact]
    public async Task GetAsync_SourceLessLegacyGoldenBlob_DecodesWithoutCurrentWriter()
    {
        var sessionId = Guid.NewGuid();
        var raw = GoldenTelemetryCompatibilityFixtures.ProcessedSourceLess.GetBytes();
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(
            PsstMetadata(sessionId, fingerprintJson: null));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);

        var telemetry = await reader.GetAsync(sessionId);

        Assert.NotNull(telemetry);
        Assert.Equal("legacy-source-less.sst", telemetry.Metadata.SourceName);
        Assert.Equal(0.02, telemetry.Metadata.Duration);
        Assert.Equal([10, 20], telemetry.Front.Travel);
        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
    }

    [Fact]
    public async Task GetAsync_RetainedSession_ReusesDecodedTelemetryForSameBlob()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(PsstMetadata(sessionId));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task GetAsync_RetainedSession_ConcurrentReadsShareOneDecode()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new BlockingTelemetryProcessor(TestTelemetryData.CreateMinimal(duration: 65));
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(PsstMetadata(sessionId));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var firstTask = Task.Run(() => reader.GetAsync(sessionId));
        Assert.True(telemetryProcessor.WaitUntilDecodeStarted(TimeSpan.FromSeconds(2)));
        var secondTask = Task.Run(() => reader.GetAsync(sessionId));

        telemetryProcessor.ReleaseDecode();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task GetAsync_UnretainedSession_DecodesEachRead()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(PsstMetadata(sessionId));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);

        var first = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task GetAsync_RetainedSession_RefreshesDecodeWhenPsstMetadataChanges()
    {
        var sessionId = Guid.NewGuid();
        var firstRaw = Blob(duration: 65);
        var secondRaw = Blob(duration: 66);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(
            PsstMetadata(sessionId, fingerprintJson: """{"fingerprint":"a"}"""),
            PsstMetadata(sessionId, fingerprintJson: """{"fingerprint":"b"}"""));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(firstRaw, secondRaw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(65, first.Metadata.Duration);
        Assert.Equal(66, second.Metadata.Duration);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task GetAsync_RetainedLegacySession_RefreshesDecodeWhenUpdatedChanges()
    {
        var sessionId = Guid.NewGuid();
        var firstRaw = Blob(duration: 65);
        var secondRaw = Blob(duration: 66);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(
            PsstMetadata(sessionId, updated: 1, fingerprintJson: null),
            PsstMetadata(sessionId, updated: 2, fingerprintJson: null));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(firstRaw, secondRaw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(65, first.Metadata.Duration);
        Assert.Equal(66, second.Metadata.Duration);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task Retain_DisposeDropsCachedDecode()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(PsstMetadata(sessionId));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
        var firstRetention = reader.Retain(sessionId);
        var first = await reader.GetAsync(sessionId);

        firstRetention.Dispose();
        using var secondRetention = reader.Retain(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task Retain_DropsCachedDecodeOnlyAfterLastRetentionDisposes()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(PsstMetadata(sessionId));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
        var firstRetention = reader.Retain(sessionId);
        var secondRetention = reader.Retain(sessionId);

        try
        {
            var first = await reader.GetAsync(sessionId);
            firstRetention.Dispose();
            var second = await reader.GetAsync(sessionId);

            secondRetention.Dispose();
            using var thirdRetention = reader.Retain(sessionId);
            var third = await reader.GetAsync(sessionId);

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.NotNull(third);
            Assert.NotSame(first, third);
            Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
            await sessionRepository.Received(3).GetSessionPsstPayloadMetadataAsync(sessionId);
            await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
        }
        finally
        {
            firstRetention.Dispose();
            secondRetention.Dispose();
        }
    }

    [Fact]
    public async Task GetAsync_RetainedSession_ClearsCachedDecodeWhenMetadataHasNoData()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(
            PsstMetadata(sessionId),
            PsstMetadata(sessionId, hasData: false),
            PsstMetadata(sessionId));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId);
        var missing = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.Null(missing);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(3).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task GetAsync_RetainedSession_EvictsFailedDecode()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new FailingThenSuccessfulTelemetryProcessor(TestTelemetryData.CreateMinimal(duration: 65));
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(PsstMetadata(sessionId));
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
        using var retention = reader.Retain(sessionId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetAsync(sessionId));
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
    }

    private static SessionProcessedTelemetryReader CreateReader(
        ISessionRepository sessionRepository,
        ISessionTelemetryProcessor telemetryProcessor) =>
        new(
            sessionRepository,
            telemetryProcessor,
            new InlineBackgroundTaskRunner());

    private static byte[] Blob(double duration) =>
        TestTelemetryData.CreateMinimal(duration: duration).BinaryForm;

    private static SessionPsstPayloadMetadata PsstMetadata(
        Guid sessionId,
        bool hasData = true,
        long updated = 1,
        string? fingerprintJson = """{"fingerprint":"same"}""") =>
        new(sessionId, hasData, updated, fingerprintJson);

    private sealed class BlockingTelemetryProcessor(TelemetryData telemetryData) : ISessionTelemetryProcessor
    {
        private readonly ManualResetEventSlim decodeStarted = new();
        private readonly ManualResetEventSlim releaseDecode = new();

        public int ReadProcessedTelemetryDataCallCount { get; private set; }

        public bool WaitUntilDecodeStarted(TimeSpan timeout) => decodeStarted.Wait(timeout);

        public void ReleaseDecode() => releaseDecode.Set();

        public double? ReadProcessedDurationSeconds(byte[]? processedData) =>
            throw new NotSupportedException();

        public TelemetryData ReadProcessedTelemetryData(byte[] processedData)
        {
            ReadProcessedTelemetryDataCallCount++;
            decodeStarted.Set();
            releaseDecode.Wait();
            return telemetryData;
        }

        public SessionSummaryMetrics ComputeSummaryMetrics(double? durationSeconds, IReadOnlyList<TrackPoint>? points) =>
            throw new NotSupportedException();

        public List<TrackPoint>? GenerateSessionTrackFromFullTrack(
            Track fullTrack,
            long? timestamp,
            double? durationSeconds,
            double gpsOffsetSeconds = 0) =>
            throw new NotSupportedException();
    }

    private sealed class FailingThenSuccessfulTelemetryProcessor(TelemetryData telemetryData) : ISessionTelemetryProcessor
    {
        public int ReadProcessedTelemetryDataCallCount { get; private set; }

        public double? ReadProcessedDurationSeconds(byte[]? processedData) =>
            throw new NotSupportedException();

        public TelemetryData ReadProcessedTelemetryData(byte[] processedData)
        {
            ReadProcessedTelemetryDataCallCount++;
            if (ReadProcessedTelemetryDataCallCount == 1)
            {
                throw new InvalidOperationException("Transient decode failure.");
            }

            return telemetryData;
        }

        public SessionSummaryMetrics ComputeSummaryMetrics(double? durationSeconds, IReadOnlyList<TrackPoint>? points) =>
            throw new NotSupportedException();

        public List<TrackPoint>? GenerateSessionTrackFromFullTrack(
            Track fullTrack,
            long? timestamp,
            double? durationSeconds,
            double gpsOffsetSeconds = 0) =>
            throw new NotSupportedException();
    }
}
