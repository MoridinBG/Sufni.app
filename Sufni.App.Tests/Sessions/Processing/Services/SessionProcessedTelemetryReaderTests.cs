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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);

        var telemetry = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
    }

    [Fact]
    public async Task GetExactAsync_UsesExpectedRevisionWithoutMetadataRetry_AndReusesRetainedDecode()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionRawPsstAsync(sessionId, 5).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetExactAsync(sessionId, processedTelemetryRevision: 5, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetExactAsync(sessionId, processedTelemetryRevision: 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId, 5);
        await sessionRepository.DidNotReceive().GetSessionPsstPayloadMetadataAsync(sessionId);
    }

    [Fact]
    public async Task GetExactAsync_ReturnsNullWithoutAdvancingToNewerRevision()
    {
        var sessionId = Guid.NewGuid();
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionRawPsstAsync(sessionId, 5).Returns((byte[]?)null);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(
            PsstMetadata(sessionId, processedTelemetryRevision: 6));
        sessionRepository.GetSessionRawPsstAsync(sessionId, 6).Returns(Blob(duration: 66));

        var result = await reader.GetExactAsync(sessionId, processedTelemetryRevision: 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId, 5);
        await sessionRepository.DidNotReceive().GetSessionRawPsstAsync(sessionId, 6);
        await sessionRepository.DidNotReceive().GetSessionPsstPayloadMetadataAsync(sessionId);
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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var firstTask = Task.Run(() => reader.GetAsync(sessionId));
        Assert.True(telemetryProcessor.WaitUntilDecodeStarted(TimeSpan.FromSeconds(2)));
        var secondTask = Task.Run(() => reader.GetAsync(sessionId));

        telemetryProcessor.ReleaseDecode();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);

        var first = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
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
            PsstMetadata(sessionId, processedTelemetryRevision: 1, fingerprintJson: """{"fingerprint":"a"}"""),
            PsstMetadata(sessionId, processedTelemetryRevision: 2, fingerprintJson: """{"fingerprint":"b"}"""));
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(firstRaw, secondRaw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(65, first.Metadata.Duration);
        Assert.Equal(66, second.Metadata.Duration);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
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
            PsstMetadata(sessionId, processedTelemetryRevision: 1, fingerprintJson: null),
            PsstMetadata(sessionId, processedTelemetryRevision: 2, fingerprintJson: null));
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(firstRaw, secondRaw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(65, first.Metadata.Duration);
        Assert.Equal(66, second.Metadata.Duration);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
    }

    [Fact]
    public async Task GetAsync_RetriesOnce_WhenRevisionChangesBetweenMetadataAndPayloadRead()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 66);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId).Returns(
            PsstMetadata(sessionId, processedTelemetryRevision: 1),
            PsstMetadata(sessionId, processedTelemetryRevision: 2));
        sessionRepository.GetSessionRawPsstAsync(sessionId, 1).Returns((byte[]?)null);
        sessionRepository.GetSessionRawPsstAsync(sessionId, 2).Returns(raw);

        var telemetry = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(telemetry);
        Assert.Equal(66, telemetry!.Metadata.Duration);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId, 1);
        await sessionRepository.Received(1).GetSessionRawPsstAsync(sessionId, 2);
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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);
        var firstRetention = reader.Retain(sessionId);
        var first = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        firstRetention.Dispose();
        using var secondRetention = reader.Retain(sessionId);
        var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);
        var firstRetention = reader.Retain(sessionId);
        var secondRetention = reader.Retain(sessionId);

        try
        {
            var first = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);
            firstRetention.Dispose();
            var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

            secondRetention.Dispose();
            using var thirdRetention = reader.Retain(sessionId);
            var third = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.NotNull(third);
            Assert.NotSame(first, third);
            Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
            await sessionRepository.Received(3).GetSessionPsstPayloadMetadataAsync(sessionId);
            await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);
        var missing = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Null(missing);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(3).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
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
        sessionRepository.GetSessionRawPsstAsync(sessionId, Arg.Any<long>()).Returns(raw);
        using var retention = reader.Retain(sessionId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken));
        var second = await reader.GetAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionPsstPayloadMetadataAsync(sessionId);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId, Arg.Any<long>());
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
        long processedTelemetryRevision = 1,
        string? fingerprintJson = """{"fingerprint":"same"}""") =>
        new(sessionId, hasData, processedTelemetryRevision, fingerprintJson);

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
