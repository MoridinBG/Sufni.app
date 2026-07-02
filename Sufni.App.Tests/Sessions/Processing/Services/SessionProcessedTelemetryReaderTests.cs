using NSubstitute;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Processing.Services;

public class SessionProcessedTelemetryReaderTests
{
    [Fact]
    public async Task GetAsync_RetainedSession_ReusesDecodedTelemetryForSameBlob()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
        await sessionRepository.Received(2).GetSessionRawPsstAsync(sessionId);
    }

    [Fact]
    public async Task GetAsync_UnretainedSession_DecodesEachRead()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw);

        var first = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
    }

    [Fact]
    public async Task GetAsync_RetainedSession_RefreshesDecodeWhenBlobHashChanges()
    {
        var sessionId = Guid.NewGuid();
        var firstRaw = Blob(duration: 65);
        var secondRaw = Blob(duration: 66);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
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
    }

    [Fact]
    public async Task Retain_DisposeDropsCachedDecode()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
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
    }

    [Fact]
    public async Task GetAsync_RetainedSession_ClearsCachedDecodeWhenBlobIsMissing()
    {
        var sessionId = Guid.NewGuid();
        var raw = Blob(duration: 65);
        var sessionRepository = Substitute.For<ISessionRepository>();
        var telemetryProcessor = new TestSessionTelemetryProcessor();
        var reader = CreateReader(sessionRepository, telemetryProcessor);
        sessionRepository.GetSessionRawPsstAsync(sessionId).Returns(raw, (byte[]?)null, raw);
        using var retention = reader.Retain(sessionId);

        var first = await reader.GetAsync(sessionId);
        var missing = await reader.GetAsync(sessionId);
        var second = await reader.GetAsync(sessionId);

        Assert.NotNull(first);
        Assert.Null(missing);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, telemetryProcessor.ReadProcessedTelemetryDataCallCount);
    }

    private static SessionProcessedTelemetryReader CreateReader(
        ISessionRepository sessionRepository,
        TestSessionTelemetryProcessor telemetryProcessor) =>
        new(
            sessionRepository,
            telemetryProcessor,
            new InlineBackgroundTaskRunner());

    private static byte[] Blob(double duration) =>
        TestTelemetryData.CreateMinimal(duration: duration).BinaryForm;
}
