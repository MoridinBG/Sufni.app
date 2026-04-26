using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Threading;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveDaqClientTests
{
    [Fact]
    public async Task StartPreviewAsync_ReturnsStarted_WhenAckAndSessionHeaderReceived()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var expectedHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 501);
        var expectedMask = LiveSensorMask.Travel | LiveSensorMask.Imu;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            var requestBytes = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            var request = Assert.IsType<LiveStartRequestFrame>(LiveProtocolReader.ParseFrame(requestBytes));
            Assert.Equal(LiveSensorMask.Travel | LiveSensorMask.Imu, request.Payload.SensorMask);
            Assert.Equal((uint)200, request.Payload.TravelHz);
            Assert.Equal((uint)100, request.Payload.ImuHz);

            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, expectedHeader.SessionId, expectedMask));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, expectedHeader));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqClientFactory(new BackgroundTaskRunner()).CreateClient();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var result = await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorMask.Travel | LiveSensorMask.Imu, 200, 100, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var started = Assert.IsType<LivePreviewStartResult.Started>(result);
        Assert.Equal(expectedHeader.SessionId, started.Header.SessionId);
        Assert.Equal(expectedHeader.AcceptedTravelHz, started.Header.AcceptedTravelHz);
        Assert.Equal(expectedHeader.ActiveImuMask, started.Header.ActiveImuMask);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartPreviewAsync_ReturnsRejected_WhenDeviceSendsErrorFrame()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateErrorFrame(1, LiveStartErrorCode.Busy));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqClientFactory(new BackgroundTaskRunner()).CreateClient();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var result = await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorMask.Travel, 100, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = Assert.IsType<LivePreviewStartResult.Rejected>(result);
        Assert.Equal(LiveStartErrorCode.Busy, rejected.ErrorCode);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
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

            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveSensorMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqClient(
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
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var failed = await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorMask.Travel, 100, 0, 0));

        Assert.IsType<LivePreviewStartResult.Failed>(failed);

        var retried = await client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorMask.Travel, 100, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<LivePreviewStartResult.Started>(retried);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Events_EmitFrameReceived_WhenFramesArrive()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 713);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveSensorMask.Travel | LiveSensorMask.Imu));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqClientFactory(new BackgroundTaskRunner()).CreateClient();
        var observedFrames = new List<LiveProtocolFrame>();
        var framesObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is not LiveDaqClientEvent.FrameReceived frameReceived)
            {
                return;
            }

            lock (observedFrames)
            {
                observedFrames.Add(frameReceived.Frame);
                if (observedFrames.Count == 2)
                {
                    framesObserved.TrySetResult();
                }
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorMask.Travel | LiveSensorMask.Imu, 200, 100, 0));
        Assert.IsType<LivePreviewStartResult.Started>(result);

        await framesObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Collection(
            observedFrames,
            frame => Assert.IsType<LiveStartAckFrame>(frame),
            frame => Assert.IsType<LiveSessionHeaderFrame>(frame));

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
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

            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveSensorMask.Gps));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.WriteAsync(CreateGpsBatchFrame(3, sessionHeader.SessionId));
            await stream.WriteAsync(CreateSessionStatsFrame(4, sessionHeader.SessionId));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqClient(
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

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorMask.Gps, 0, 0, 10));
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var counters = await dropObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await statusObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal((ulong)1, counters.RawTelemetryFramesSkipped);
        Assert.False(telemetryFrameObserved);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
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

            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveSensorMask.Gps));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.WriteAsync(CreateGpsBatchFrame(3, sessionHeader.SessionId));
            await stream.WriteAsync(CreateSessionStatsFrame(4, sessionHeader.SessionId));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqClient(
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

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorMask.Gps, 0, 0, 10));
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var counters = await dropObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await statusObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal((ulong)1, counters.ParsedTelemetryFramesDropped);
        Assert.False(telemetryFrameObserved);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
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

            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveSensorMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();

            var stopBytes = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize);
            Assert.IsType<LiveStopRequestFrame>(LiveProtocolReader.ParseFrame(stopBytes));

            await stream.WriteAsync(LiveProtocolTestFrames.CreateStopAckFrame(3, sessionHeader.SessionId));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqClientFactory(new BackgroundTaskRunner()).CreateClient();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorMask.Travel, 100, 0, 0));
        Assert.IsType<LivePreviewStartResult.Started>(started);

        await client.StopPreviewAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_WithoutActiveSession_ShortCircuits()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptTask = listener.AcceptTcpClientAsync();

        await using var client = new LiveDaqClient();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        await client.StopPreviewAsync().WaitAsync(TimeSpan.FromSeconds(1));

        await client.DisconnectAsync();
        using var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_WhenStopAckTimesOut_DoesNotHang()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 612);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize + LiveProtocolConstants.StartRequestPayloadSize);
            await stream.WriteAsync(LiveProtocolTestFrames.CreateStartAckFrame(1, LiveStartErrorCode.Ok, sessionHeader.SessionId, LiveSensorMask.Travel));
            await stream.WriteAsync(LiveProtocolTestFrames.CreateSessionHeaderFrame(2, sessionHeader));
            await stream.FlushAsync();

            // Deliberately consume STOP_LIVE without ever replying with STOP_ACK
            // so the client has to fall back to its bounded timeout.
            _ = await ReadExactAsync(stream, LiveProtocolConstants.FrameHeaderSize);
            await Task.Delay(500);
        });

        await using var client = new LiveDaqClient(TimeSpan.FromMilliseconds(150));
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorMask.Travel, 100, 0, 0));
        Assert.IsType<LivePreviewStartResult.Started>(started);

        await client.StopPreviewAsync().WaitAsync(TimeSpan.FromSeconds(2));

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConnectAsync_DisposesTcpClient_WhenConnectFails()
    {
        var createdClients = new List<TrackingTcpClient>();

        await using var client = new LiveDaqClient(
            TimeSpan.FromSeconds(1),
            () =>
            {
                var tcpClient = new TrackingTcpClient();
                createdClients.Add(tcpClient);
                return tcpClient;
            });

        var unusedPort = GetUnusedPort();

        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), unusedPort));

        var failedClient = Assert.Single(createdClients);
        Assert.True(failedClient.WasDisposed);
    }

    [Fact]
    public async Task DisposeAsync_WhenConnected_CompletesEvents_DisposesTcpClient_AndFutureConnectThrows()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();
        var createdClients = new List<TrackingTcpClient>();

        var client = new LiveDaqClient(
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

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        await client.DisposeAsync();
        await eventsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await client.DisposeAsync();

        var trackingClient = Assert.Single(createdClients);
        Assert.True(trackingClient.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), port));
    }

    [Fact]
    public async Task CreateClient_ReturnsDistinctInstances()
    {
        var factory = new LiveDaqClientFactory(new BackgroundTaskRunner());

        await using var first = factory.CreateClient();
        await using var second = factory.CreateClient();

        Assert.NotSame(first, second);
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

    private static byte[] CreateSessionStatsFrame(uint sequence, uint sessionId)
    {
        var payload = new byte[LiveProtocolConstants.SessionStatsPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), 6);
        return LiveProtocolReader.CreateFrame(LiveFrameType.SessionStats, sequence, payload);
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
}