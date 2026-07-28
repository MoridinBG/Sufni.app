using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Threading;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Tests.TestSupport.LiveDaq;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqV2ClientTests
{
    [Fact]
    public async Task StartPreviewAsync_ReturnsStarted_WhenAckAndSessionHeaderReceived()
    {
        var expectedHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(
            sessionId: 501,
            requestedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu,
            acceptedSensorMask: LiveSensorInstanceMask.ForkTravel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.RearImu);
        var expectedMask = LiveStreamMask.Travel | LiveStreamMask.Imu;
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV2Client>(() => new LiveDaqV2Client());

        var result = await harness.WithServerAsync(
            async stream =>
            {
                var requestBytes = await LiveDaqClientProtocolHarness<LiveDaqV2Client>.ReadExactAsync(
                    stream,
                    LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
                var request = Assert.IsType<LiveStartRequestFrame>(LiveV2ProtocolReader.ParseFrame(requestBytes));
                Assert.Equal(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu, request.Payload.RequestedSensorMask);
                Assert.Equal((uint)200_000, request.Payload.TravelRateMhz);
                Assert.Equal((uint)100_000, request.Payload.ImuRateMhz);

                await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, expectedHeader.SessionId, expectedMask));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, expectedHeader));
                await stream.FlushAsync();
            },
            async (client, port) =>
            {
                await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
                return await client.StartPreviewAsync(
                        new LiveStartRequest(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu, 200_000, 100_000, 0))
                    .AwaitBoundedAsync(TimeSpan.FromSeconds(2));
            });

        var started = Assert.IsType<LivePreviewStartResult.Started>(result);
        Assert.Equal(expectedHeader.SessionId, started.Header.SessionId);
        Assert.Equal(expectedHeader.AcceptedTravelHz, started.Header.AcceptedTravelHz);
        Assert.Equal(expectedHeader.ActiveImuMask, started.Header.ActiveImuMask);
        Assert.Equal(expectedHeader.AcceptedSensorMask, started.Header.AcceptedSensorMask);
        Assert.Equal(expectedMask, started.Header.AcceptedStreamMask);
        Assert.Equal(LiveSensorInstanceMask.ShockTravel | LiveSensorInstanceMask.ForkImu, started.Header.MissingSensorMask);
    }

    [Fact]
    public async Task StartPreviewAsync_ReturnsFailed_AndDoesNotEmitHeader_WhenAckSessionDoesNotMatchHeader()
    {
        var wireHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 502);
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV2Client>(() => new LiveDaqV2Client());
        var stopReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var outcome = await harness.WithServerAsync(
            async stream =>
            {
                _ = await LiveDaqClientProtocolHarness<LiveDaqV2Client>.ReadExactAsync(
                    stream,
                    LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
                await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(
                    1,
                    LiveStartErrorCode.Ok,
                    sessionId: 501,
                    selectedStreamMask: LiveStreamMask.Temperature | LiveStreamMask.Marker));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, wireHeader));
                await stream.FlushAsync();
                await stopReturned.Task;
                Assert.False(stream.DataAvailable);
            },
            async (client, port) =>
            {
                var observedHeaders = new List<LiveSessionHeader>();
                using var subscription = client.Events.Subscribe(clientEvent =>
                {
                    if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveSessionHeaderFrame headerFrame })
                    {
                        observedHeaders.Add(headerFrame.Payload);
                    }
                });
                await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
                var result = await client.StartPreviewAsync(
                        new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
                    .AwaitBoundedAsync(TimeSpan.FromSeconds(2));
                await client.StopPreviewAsync().AwaitBoundedAsync(TimeSpan.FromSeconds(1));
                stopReturned.TrySetResult();
                return (Result: result, ObservedHeaders: observedHeaders);
            });

        Assert.IsType<LivePreviewStartResult.Failed>(outcome.Result);
        Assert.Empty(outcome.ObservedHeaders);
    }

    [Theory]
    [InlineData(StartRejectSource.StartAck, LiveStartErrorCode.NoSensorsStarted)]
    [InlineData(StartRejectSource.ErrorFrame, LiveStartErrorCode.Busy)]
    public async Task StartPreviewAsync_ReturnsRejected_WhenDeviceRejectsStart(
        StartRejectSource source,
        LiveStartErrorCode expectedError)
    {
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV2Client>(() => new LiveDaqV2Client());

        var result = await harness.WithServerAsync(
            async stream =>
            {
                _ = await LiveDaqClientProtocolHarness<LiveDaqV2Client>.ReadExactAsync(
                    stream,
                    LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
                var frame = source is StartRejectSource.StartAck
                    ? LiveProtocolTestFrames.CreateStartAckFrame(1, expectedError, sessionId: 0, selectedStreamMask: LiveStreamMask.None)
                    : LiveProtocolTestFrames.CreateErrorFrame(1, expectedError);
                await stream.WriteAsync(frame);
                await stream.FlushAsync();
            },
            async (client, port) =>
            {
                await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
                return await client.StartPreviewAsync(
                        new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
                    .AwaitBoundedAsync(TimeSpan.FromSeconds(2));
            });

        var rejected = Assert.IsType<LivePreviewStartResult.Rejected>(result);
        Assert.Equal(expectedError, rejected.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(rejected.UserMessage));
    }

    [Fact]
    public async Task StartPreviewAsync_AllowsRetry_WhenInitialSendFails()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 888);
        var sendAttempt = 0;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client(
            TimeSpan.FromSeconds(1),
            () => new TcpClient(),
            async (stream, frame, cancellationToken) =>
            {
                if (Interlocked.Increment(ref sendAttempt) == 1)
                {
                    throw new IOException("Injected send failure");
                }

                await stream.WriteAsync(frame, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            });
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);

        var failed = await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<LivePreviewStartResult.Failed>(failed);

        var retried = await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken)
            .AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<LivePreviewStartResult.Started>(retried);

        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Events_EmitFrameReceived_WhenFramesArrive()
    {
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 713);
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV2Client>(() => new LiveDaqV2Client());

        var observedFrames = await harness.WithServerAsync(
            async stream =>
            {
                _ = await LiveDaqClientProtocolHarness<LiveDaqV2Client>.ReadExactAsync(
                    stream,
                    LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
                await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Travel | LiveStreamMask.Imu));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
                await stream.FlushAsync();
            },
            async (client, port) =>
            {
                await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
                return await LiveDaqClientProtocolHarness<LiveDaqV2Client>.ObserveFramesAsync(
                    client,
                    expectedCount: 2,
                    async () =>
                    {
                        var result = await client.StartPreviewAsync(
                            new LiveStartRequest(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu, 200_000, 100_000, 0));
                        Assert.IsType<LivePreviewStartResult.Started>(result);
                    });
            });

        Assert.Collection(
            observedFrames,
            frame => Assert.IsType<LiveStartAckFrame>(frame),
            frame => Assert.Equal(
                LiveStreamMask.Travel | LiveStreamMask.Imu,
                Assert.IsType<LiveSessionHeaderFrame>(frame).Payload.AcceptedStreamMask));
    }

    [Fact]
    public async Task Events_EmitOnlyActiveSessionTelemetryAndStatistics()
    {
        const uint activeSessionId = 713;
        const uint foreignSessionId = 714;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: activeSessionId);
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV2Client>(() => new LiveDaqV2Client());

        var observedFrames = await harness.WithServerAsync(
            async stream =>
            {
                _ = await LiveDaqClientProtocolHarness<LiveDaqV2Client>.ReadExactAsync(
                    stream,
                    LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
                await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(
                    1,
                    LiveStartErrorCode.Ok,
                    activeSessionId,
                    LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateTravelBatchFrame(3, foreignSessionId, new LiveTravelRecord(1, 2)));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateTravelBatchFrame(4, activeSessionId, new LiveTravelRecord(3, 4)));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateImuBatchFrame(5, foreignSessionId, new ImuRecord(1, 2, 3, 4, 5, 6)));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateImuBatchFrame(6, activeSessionId, new ImuRecord(7, 8, 9, 10, 11, 12)));
                await stream.WriteAsync(CreateGpsBatchFrame(7, foreignSessionId));
                await stream.WriteAsync(CreateGpsBatchFrame(8, activeSessionId));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionStatsFrame(9, foreignSessionId));
                await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionStatsFrame(10, activeSessionId));
                await stream.FlushAsync();
            },
            async (client, port) =>
            {
                var sessionFrames = new List<LiveProtocolFrame>();
                var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var subscription = client.Events.Subscribe(clientEvent =>
                {
                    if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: var frame } &&
                        frame is LiveTravelBatchFrame or LiveImuBatchFrame or LiveGpsBatchFrame or LiveSessionStatsFrame)
                    {
                        lock (sessionFrames)
                        {
                            sessionFrames.Add(frame);
                        }
                    }
                    else if (clientEvent is LiveDaqClientEvent.Disconnected)
                    {
                        disconnected.TrySetResult();
                    }
                });

                await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
                Assert.IsType<LivePreviewStartResult.Started>(await client.StartPreviewAsync(
                    new LiveStartRequest(LiveSensorInstanceMask.All, 200_000, 100_000, 10_000)));
                await disconnected.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
                return sessionFrames;
            });

        Assert.Equal(4, observedFrames.Count);
        Assert.Contains(observedFrames, frame => frame is LiveTravelBatchFrame travel && travel.Batch.SessionId == activeSessionId);
        Assert.Contains(observedFrames, frame => frame is LiveImuBatchFrame imu && imu.Batch.SessionId == activeSessionId);
        Assert.Contains(observedFrames, frame => frame is LiveGpsBatchFrame gps && gps.Batch.SessionId == activeSessionId);
        Assert.Contains(observedFrames, frame => frame is LiveSessionStatsFrame stats && stats.Payload.SessionId == activeSessionId);
    }

    [Fact]
    public async Task ReceiveLoop_SkipsTelemetryPayload_WhenRawCapacityUnavailable_AndReadsFollowingStatusFrame()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 901);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Gps));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.WriteAsync(CreateGpsBatchFrame(3, sessionHeader.SessionId));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionStatsFrame(4, sessionHeader.SessionId));
            await stream.FlushAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client(
            TimeSpan.FromSeconds(1),
            () => new TcpClient(),
            SendFrameForTestAsync,
            rawTelemetryFrameCapacity: 0);
        var statusObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropObserved = new TaskCompletionSource<LiveDaqClientDropCounters>(TaskCreationOptions.RunContinuationsAsynchronously);
        var telemetryFrameObserved = false;
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            switch (clientEvent)
            {
                case LiveDaqClientEvent.DropCountersChanged countersChanged
                    when countersChanged.Counters.RawTelemetryFramesSkipped > 0:
                    dropObserved.TrySetResult(countersChanged.Counters);
                    break;

                case LiveDaqClientEvent.FrameReceived { Frame: LiveGpsBatchFrame }:
                    telemetryFrameObserved = true;
                    break;

                case LiveDaqClientEvent.FrameReceived { Frame: LiveSessionStatsFrame }:
                    statusObserved.TrySetResult();
                    break;
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Gps, 0, 0, 10_000), cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var counters = await dropObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await statusObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal((ulong)1, counters.RawTelemetryFramesSkipped);
        Assert.False(telemetryFrameObserved);

        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ParseLoop_DropsTelemetryFrame_WhenParsedCapacityUnavailable_AndReadsFollowingStatusFrame()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 902);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Gps));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.WriteAsync(CreateGpsBatchFrame(3, sessionHeader.SessionId));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionStatsFrame(4, sessionHeader.SessionId));
            await stream.FlushAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client(
            TimeSpan.FromSeconds(1),
            () => new TcpClient(),
            SendFrameForTestAsync,
            rawTelemetryFrameCapacity: 1,
            parsedTelemetryFrameCapacity: 0);
        var statusObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropObserved = new TaskCompletionSource<LiveDaqClientDropCounters>(TaskCreationOptions.RunContinuationsAsynchronously);
        var telemetryFrameObserved = false;
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            switch (clientEvent)
            {
                case LiveDaqClientEvent.DropCountersChanged countersChanged
                    when countersChanged.Counters.ParsedTelemetryFramesDropped > 0:
                    dropObserved.TrySetResult(countersChanged.Counters);
                    break;

                case LiveDaqClientEvent.FrameReceived { Frame: LiveGpsBatchFrame }:
                    telemetryFrameObserved = true;
                    break;

                case LiveDaqClientEvent.FrameReceived { Frame: LiveSessionStatsFrame }:
                    statusObserved.TrySetResult();
                    break;
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Gps, 0, 0, 10_000), cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var counters = await dropObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await statusObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        Assert.Equal((ulong)1, counters.ParsedTelemetryFramesDropped);
        Assert.False(telemetryFrameObserved);

        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_WaitsForStopAck()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 612);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();

            var stopBytes = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize);
            Assert.IsType<LiveStopRequestFrame>(LiveV2ProtocolReader.ParseFrame(stopBytes));

            await stream.WriteAsync(LiveProtocolTestFrames.CreateStopAckFrame(3, sessionHeader.SessionId));
            await stream.FlushAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);

        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(started);

        await client.StopPreviewAsync(cancellationToken: TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_IgnoresForeignAck_UntilActiveSessionAckArrives()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 612);
        var releaseMatchingAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStopAckFrame(3, sessionHeader.SessionId + 1));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionStatsFrame(4, sessionHeader.SessionId));
            await stream.FlushAsync();

            await releaseMatchingAck.Task;
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStopAckFrame(5, sessionHeader.SessionId));
            await stream.FlushAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client();
        var statsObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var matchingAckObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedStopAcks = new List<uint>();
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveSessionStatsFrame })
            {
                statsObserved.TrySetResult();
            }
            else if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveStopAckFrame stopAckFrame })
            {
                observedStopAcks.Add(stopAckFrame.Payload.SessionId);
                matchingAckObserved.TrySetResult();
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken));

        var stopTask = client.StopPreviewAsync(cancellationToken: TestContext.Current.CancellationToken);
        await statsObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.False(stopTask.IsCompleted);

        releaseMatchingAck.TrySetResult();
        await stopTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await matchingAckObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([sessionHeader.SessionId], observedStopAcks);

        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_WithoutActiveSession_ShortCircuits()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptTask = listener.AcceptTcpClientAsync(cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);

        await client.StopPreviewAsync(cancellationToken: TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(1));

        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        using var accepted = await acceptTask.AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_WhenStopAckTimesOut_DoesNotHang()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 612);
        var stopRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();

            // Deliberately consume STOP_LIVE without ever replying with STOP_ACK
            // so the client has to fall back to its bounded timeout.
            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize);
            stopRequestReceived.TrySetResult();
            await releaseServer.Task;
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client(TimeSpan.FromMilliseconds(150));
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);

        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var stopTask = client.StopPreviewAsync(cancellationToken: TestContext.Current.CancellationToken);
        await stopRequestReceived.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await stopTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        releaseServer.TrySetResult();
        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartPreviewAsync_CompletesFailed_WhenDelayedStopAckArrivesDuringStart()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var initialHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 612);
        var retryHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 614);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, initialHeader.SessionId, LiveStreamMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, initialHeader));
            await stream.FlushAsync();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize);
            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStopAckFrame(3, initialHeader.SessionId));
            await stream.FlushAsync();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(4, LiveStartErrorCode.Ok, retryHeader.SessionId, LiveStreamMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(5, retryHeader));
            await stream.FlushAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client(TimeSpan.FromMilliseconds(100));
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<LivePreviewStartResult.Started>(await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken));

        await client.StopPreviewAsync(cancellationToken: TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        var interrupted = await client.StartPreviewAsync(
                new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken)
            .AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Failed>(interrupted);

        var retried = await client.StartPreviewAsync(
                new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0), cancellationToken: TestContext.Current.CancellationToken)
            .AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Started>(retried);

        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_SendsStop_WhenSessionHeaderArrivesAfterStartWaitCancellation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 615);
        var releaseHeader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize + LiveV2ProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveStreamMask.Travel));
            await stream.FlushAsync();
            await releaseHeader.Task;
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionStatsFrame(3, sessionHeader.SessionId));
            await stream.FlushAsync();

            var stopBytes = await ReadExactAsync(stream, LiveV2ProtocolConstants.FrameHeaderSize);
            Assert.IsType<LiveStopRequestFrame>(LiveV2ProtocolReader.ParseFrame(stopBytes));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStopAckFrame(4, sessionHeader.SessionId));
            await stream.FlushAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        await using var client = new LiveDaqV2Client();
        var ackObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var headerObserved = new TaskCompletionSource<LiveSessionHeader>(TaskCreationOptions.RunContinuationsAsynchronously);
        var statsObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveStartAckFrame })
            {
                ackObserved.TrySetResult();
            }
            else if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveSessionHeaderFrame headerFrame })
            {
                headerObserved.TrySetResult(headerFrame.Payload);
            }
            else if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveSessionStatsFrame })
            {
                statsObserved.TrySetResult();
            }
        });
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var start = client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0),
            cancellation.Token);
        await ackObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

        releaseHeader.SetResult();
        var observedHeader = await headerObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(sessionHeader.SessionId, observedHeader.SessionId);
        await statsObserved.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await client.StopPreviewAsync(cancellationToken: TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        await client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await serverTask.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConnectAsync_DisposesTcpClient_WhenConnectFails()
    {
        var createdClients = new List<TrackingTcpClient>();

        await using var client = new LiveDaqV2Client(
            TimeSpan.FromSeconds(1),
            () =>
            {
                var tcpClient = new TrackingTcpClient();
                createdClients.Add(tcpClient);
                return tcpClient;
            });

        var unusedPort = GetUnusedPort();

        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), unusedPort, cancellationToken: TestContext.Current.CancellationToken));

        var failedClient = Assert.Single(createdClients);
        Assert.True(failedClient.WasDisposed);
    }

    [Fact]
    public async Task DisposeAsync_WhenConnected_CompletesEvents_DisposesTcpClient_AndFutureConnectThrows()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync(cancellationToken: TestContext.Current.CancellationToken);
        var createdClients = new List<TrackingTcpClient>();

        var client = new LiveDaqV2Client(
            TimeSpan.FromSeconds(1),
            () =>
            {
                var tcpClient = new TrackingTcpClient();
                createdClients.Add(tcpClient);
                return tcpClient;
            });
        var eventsCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(
            _ => { },
            ex => eventsCompleted.TrySetException(ex),
            () => eventsCompleted.TrySetResult());

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken);
        using var accepted = await acceptTask.AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(2));

        await client.DisposeAsync();
        await eventsCompleted.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        await client.DisposeAsync();

        var trackingClient = Assert.Single(createdClients);
        Assert.True(trackingClient.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), port, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead));
            if (read == 0)
            {
                throw new EndOfStreamException($"Expected {length} bytes but stream closed after {totalRead}.");
            }

            totalRead += read;
        }

        return buffer;
    }

    private static byte[] CreateGpsBatchFrame(uint sequence, uint sessionId)
    {
        return LiveProtocolTestFrames.CreateGpsBatchFrame(
            sequence,
            sessionId,
            new GpsRecord(
                Timestamp: new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                Latitude: 42.6977,
                Longitude: 23.3219,
                Altitude: 600,
                Speed: 10,
                Heading: 90,
                FixMode: 3,
                Satellites: 12,
                Epe2d: 0.5f,
                Epe3d: 0.8f));
    }

    private static async Task SendFrameForTestAsync(NetworkStream stream, byte[] frame, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private sealed class TrackingTcpClient : TcpClient
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    public enum StartRejectSource
    {
        StartAck,
        ErrorFrame
    }
}
