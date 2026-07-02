using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Common;
using Sufni.Telemetry;
using static Sufni.App.Tests.LiveDaq.Services.LiveStreaming.LiveV3ProtocolTestFrames;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqV3ClientTests
{
    [Fact]
    public async Task ConnectAsync_SendsHandshakeBeforeAnyFrame()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            var handshake = await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize);
            await stream.WriteAsync(LiveV3ProtocolReader.CreateServerHello(0x0102030405060708));
            await stream.FlushAsync();
            return handshake;
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var handshake = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(LiveV3ProtocolReader.CreateHandshake(), handshake);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task ConnectAsync_RejectsInvalidServerHello()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            _ = await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize);
            var hello = LiveV3ProtocolReader.CreateServerHello(0x0102030405060708);
            hello[4] = 4;
            await stream.WriteAsync(hello);
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client();

        await Assert.ThrowsAsync<FormatException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), port));
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConnectAsync_RejectsHelloWithMismatchedDiscoveredBoardId()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var expectedBoardId = UuidUtil.CreateDeviceUuid("0102030405060708").ToString();

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            _ = await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize);
            await stream.WriteAsync(LiveV3ProtocolReader.CreateServerHello(0x1112131415161718));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client(expectedBoardId);

        await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), port));
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartPreviewAsync_LoadsCapabilitiesBeforeStart_AndWaitsForSessionHeader()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var startResultSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSessionHeader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();

            Assert.Equal(LiveV3ProtocolReader.CreateHandshake(), await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
            await stream.WriteAsync(LiveV3ProtocolReader.CreateServerHello(0x0102030405060708));
            await stream.FlushAsync();

            var capabilitiesRequest = Assert.IsType<LiveV3CapabilitiesRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
            Assert.Equal((uint)1, capabilitiesRequest.Sequence);
            await stream.WriteAsync(CreateCapabilitiesFrame(2));
            await stream.FlushAsync();

            var startRequest = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
            Assert.Equal((uint)2, startRequest.Sequence);
            Assert.Equal(
                [SstV5ProtocolConstants.StreamTravel, SstV5ProtocolConstants.StreamImu, SstV5ProtocolConstants.StreamGps],
                startRequest.Payload.StreamRequests.Select(record => record.StreamKind).ToArray());
            Assert.DoesNotContain(startRequest.Payload.StreamRequests, record => record.StreamKind is SstV5ProtocolConstants.StreamBattery or SstV5ProtocolConstants.StreamMarker);
            Assert.Equal(LiveSensorInstanceMask.Travel, startRequest.Payload.StreamRequests[0].SourceMask);
            Assert.Equal((uint)200_000, startRequest.Payload.StreamRequests[0].RateMhz);
            Assert.Equal((uint)50, startRequest.Payload.StreamRequests[0].BatchDurationMs);
            Assert.Equal(LiveSensorInstanceMask.Imu, startRequest.Payload.StreamRequests[1].SourceMask);
            Assert.Equal((uint)100_000, startRequest.Payload.StreamRequests[1].RateMhz);
            Assert.Equal(LiveSensorInstanceMask.Gps, startRequest.Payload.StreamRequests[2].SourceMask);
            Assert.Equal((uint)5_000, startRequest.Payload.StreamRequests[2].RateMhz);
            Assert.Equal(SstV5ProtocolConstants.ExtensionGpsDiagPublicV1, startRequest.Payload.StreamRequests[2].ExtensionMask);

            await stream.WriteAsync(CreateStartResultFrame(4, resultCode: 0, sessionId: SessionId, acceptedStreamMask: LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps));
            await stream.FlushAsync();
            startResultSent.TrySetResult();

            await allowSessionHeader.Task;
            await stream.WriteAsync(CreateSessionHeaderFrame(5, TravelStream(), ImuStream(), GpsStream()));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var startTask = client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps, 200_000, 100_000, 5_000));

        await startResultSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(startTask.IsCompleted);
        allowSessionHeader.TrySetResult();

        var result = await startTask.WaitAsync(TimeSpan.FromSeconds(2));
        var started = Assert.IsType<LivePreviewStartResult.Started>(result);
        Assert.Equal(SessionId, started.Header.SessionId);
        Assert.Equal(LiveProtocolVersion.V3, started.Header.ProtocolVersion);
        Assert.Equal((uint)200_000, started.Header.AcceptedTravelRateMhz);
        Assert.Equal((uint)100_000, started.Header.AcceptedImuRateMhz);
        Assert.Equal((uint)5_000, started.Header.AcceptedGpsRateMhz);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RequestDeviceStateAsync_SendsRequestAndReturnsResponse_BeforeSession()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            Assert.Equal(LiveV3ProtocolReader.CreateHandshake(), await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
            await stream.WriteAsync(LiveV3ProtocolReader.CreateServerHello(0x0102030405060708));
            await stream.FlushAsync();

            var request = Assert.IsType<LiveV3DeviceStateRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
            Assert.Equal((uint)1, request.Sequence);
            await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                LiveV3FrameType.DeviceStateResp,
                0,
                2,
                CreateDeviceStatePayload()));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var state = await client.RequestDeviceStateAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SstV5ProtocolConstants.StreamTravel, Assert.Single(state.Streams).StreamKind);
        Assert.True(Assert.Single(state.Sources).Available);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RequestDeviceStateAsync_RejectsSecondOutstandingRequest()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var allowResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            Assert.Equal(LiveV3ProtocolReader.CreateHandshake(), await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
            await stream.WriteAsync(LiveV3ProtocolReader.CreateServerHello(0x0102030405060708));
            await stream.FlushAsync();
            _ = await ReadFrameAsync(stream);
            await allowResponse.Task;
            await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                LiveV3FrameType.DeviceStateResp,
                0,
                2,
                CreateDeviceStatePayload()));
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var firstRequest = client.RequestDeviceStateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RequestDeviceStateAsync());

        allowResponse.TrySetResult();
        _ = await firstRequest.WaitAsync(TimeSpan.FromSeconds(2));
        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartPreviewAsync_ReturnsRejected_WhenStartResultDenied()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartupServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(CreateStartResultFrame(
                    4,
                    resultCode: 1,
                    sessionId: 0,
                    acceptedStreamMask: LiveStreamMask.None,
                    (SstV5ProtocolConstants.StreamTravel, LiveV3ProtocolHelpers.AdmissionNoTelemetry, LiveSensorInstanceMask.Travel)));
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = Assert.IsType<LivePreviewStartResult.Rejected>(result);
        Assert.Equal(LiveStartErrorCode.NoSensorsStarted, rejected.ErrorCode);
        var reason = Assert.Single(rejected.AdmissionReasons);
        Assert.Equal(LiveV3ProtocolHelpers.AdmissionNoTelemetry, reason.Reason);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartPreviewAsync_ReturnsFailed_WhenErrorArrivesBeforeSessionHeader()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartupServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(CreateErrorFrame(4, errorCode: 99));
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<LivePreviewStartResult.Failed>(result);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartPreviewAsync_ReturnsFailed_WhenSessionResultArrivesBeforeSessionHeader()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartupServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(CreateStartResultFrame(4, resultCode: 0, sessionId: SessionId, acceptedStreamMask: LiveStreamMask.Travel));
                await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.SessionResult,
                    SessionId,
                    5,
                    CreateSessionResultPayload(streamStatusCount: 0, sessionResultReason: 5)));
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<LivePreviewStartResult.Failed>(result);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Events_EmitCanonicalFrames_ForDataStatusAndSessionResult()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartedServerAsync(
            listener,
            [TravelStream(), GpsStream()],
            async stream =>
            {
                await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.TravelData,
                    SessionId,
                    6,
                    CreateTravelDataPayload(0, 1_000, (100, 200))));
                await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.Status,
                    SessionId,
                    7,
                    CreateStatusPayload()));
                await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.SessionResult,
                    SessionId,
                    8,
                    CreateSessionResultPayload()));
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        var observed = new HashSet<Type>();
        var framesObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is not LiveDaqClientEvent.FrameReceived frameReceived)
            {
                return;
            }

            lock (observed)
            {
                observed.Add(frameReceived.Frame.GetType());
                if (observed.Contains(typeof(LiveTravelBatchFrame)) &&
                    observed.Contains(typeof(LiveStatusFrame)) &&
                    observed.Contains(typeof(LiveSessionResultFrame)))
                {
                    framesObserved.TrySetResult();
                }
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Gps, 100_000, 0, 5_000))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Started>(result);

        await framesObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Events_SkipUnknownServerFrame_AndContinueStreaming()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartedServerAsync(
            listener,
            [TravelStream()],
            async stream =>
            {
                await stream.WriteAsync(CreateUnknownFrame(rawFrameType: 0xEE, sessionId: SessionId, sequence: 6, payload: [1, 2, 3]));
                await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.TravelData,
                    SessionId,
                    7,
                    CreateTravelDataPayload(0, 1_000, (100, 200))));
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        var travelFrameObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveTravelBatchFrame })
            {
                travelFrameObserved.TrySetResult();
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Started>(result);

        await travelFrameObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopPreviewAsync_WaitsForStopResultAndSessionResult()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var stopResultSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSessionResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = RunStartedServerAsync(
            listener,
            [TravelStream(), GpsStream()],
            async stream =>
            {
                var stopRequest = Assert.IsType<LiveStopRequestFrame>(
                    LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
                Assert.Equal((uint)3, stopRequest.Sequence);

                await stream.WriteAsync(CreateStopResultFrame(5));
                await stream.FlushAsync();
                stopResultSent.TrySetResult();

                await allowSessionResult.Task;
                await stream.WriteAsync(LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.SessionResult,
                    SessionId,
                    6,
                    CreateSessionResultPayload()));
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Gps, 100_000, 0, 5_000))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var stopTask = client.StopPreviewAsync();
        await stopResultSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(stopTask.IsCompleted);

        allowSessionResult.TrySetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(2));

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ErrorDuringActiveStream_FaultsClient()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartedServerAsync(
            listener,
            [TravelStream()],
            async stream =>
            {
                await stream.WriteAsync(CreateErrorFrame(6, errorCode: 42));
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        var faulted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.Faulted faultedEvent)
            {
                faulted.TrySetResult(faultedEvent.ErrorMessage);
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var errorMessage = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task RunStartupServerAsync(
        TcpListener listener,
        Func<NetworkStream, Task> afterStartRequestAsync)
    {
        using var serverClient = await listener.AcceptTcpClientAsync();
        await using var stream = serverClient.GetStream();
        await ReadHandshakeCapabilitiesAndStartAsync(stream);
        await afterStartRequestAsync(stream);
    }

    private static async Task RunStartedServerAsync(
        TcpListener listener,
        V3StreamSpec[] streams,
        Func<NetworkStream, Task> afterStartedAsync)
    {
        using var serverClient = await listener.AcceptTcpClientAsync();
        await using var stream = serverClient.GetStream();
        await ReadHandshakeCapabilitiesAndStartAsync(stream);
        await stream.WriteAsync(CreateStartResultFrame(4, resultCode: 0, sessionId: SessionId, acceptedStreamMask: LiveStreamMask.Travel | LiveStreamMask.Gps));
        await stream.WriteAsync(CreateSessionHeaderFrame(5, streams));
        await stream.FlushAsync();
        await afterStartedAsync(stream);
    }

    private static async Task ReadHandshakeCapabilitiesAndStartAsync(NetworkStream stream)
    {
        Assert.Equal(LiveV3ProtocolReader.CreateHandshake(), await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
        await stream.WriteAsync(LiveV3ProtocolReader.CreateServerHello(0x0102030405060708));
        await stream.FlushAsync();
        Assert.IsType<LiveV3CapabilitiesRequestFrame>(
            LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
        await stream.WriteAsync(CreateCapabilitiesFrame(2));
        await stream.FlushAsync();
        Assert.IsType<LiveV3StartRequestFrame>(
            LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
    }

    private static byte[] CreateCapabilitiesFrame(uint sequence) =>
        LiveV3ProtocolReader.CreateFrame(
            LiveV3FrameType.CapabilitiesResp,
            0,
            sequence,
            CreateCapabilitiesPayload(
                LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps,
                LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps,
                (SstV5ProtocolConstants.StreamTravel, LiveSensorInstanceMask.Travel, 100_000u, 500_000u),
                (SstV5ProtocolConstants.StreamImu, LiveSensorInstanceMask.Imu, 100_000u, 500_000u),
                (SstV5ProtocolConstants.StreamGps, LiveSensorInstanceMask.Gps, 1_000u, 10_000u)));

    private static byte[] CreateStartResultFrame(
        uint sequence,
        byte resultCode,
        byte sessionId,
        LiveStreamMask acceptedStreamMask,
        params (byte StreamKind, byte Reason, LiveSensorInstanceMask Sources)[] reasons)
    {
        var payload = new byte[LiveV3ProtocolConstants.StartResultHeaderSize +
                               reasons.Length * LiveV3ProtocolConstants.AdmissionReasonRecordSize];
        payload[0] = resultCode;
        payload[1] = sessionId;
        payload[2] = (byte)reasons.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)acceptedStreamMask);

        var offset = LiveV3ProtocolConstants.StartResultHeaderSize;
        foreach (var reason in reasons)
        {
            payload[offset] = 2;
            payload[offset + 1] = reason.StreamKind;
            payload[offset + 2] = reason.Reason;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 4, 4), (uint)reason.Sources);
            offset += LiveV3ProtocolConstants.AdmissionReasonRecordSize;
        }

        return LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.StartResult, 0, sequence, payload);
    }

    private static byte[] CreateSessionHeaderFrame(uint sequence, params V3StreamSpec[] streams) =>
        LiveV3ProtocolReader.CreateFrame(
            LiveV3FrameType.SessionHeader,
            SessionId,
            sequence,
            CreateSessionHeaderPayload(
                LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps,
                streams));

    private static byte[] CreateStopResultFrame(uint sequence)
    {
        var payload = new byte[LiveV3ProtocolConstants.StopResultPayloadSize];
        return LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.StopResult, SessionId, sequence, payload);
    }

    private static byte[] CreateErrorFrame(uint sequence, byte errorCode)
    {
        var payload = new byte[LiveV3ProtocolConstants.ErrorPayloadSize];
        payload[0] = errorCode;
        return LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.Error, SessionId, sequence, payload);
    }

    private static byte[] CreateUnknownFrame(byte rawFrameType, byte sessionId, uint sequence, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[LiveV3ProtocolConstants.FrameHeaderSize + payload.Length];
        frame[0] = rawFrameType;
        frame[2] = sessionId;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), sequence);
        payload.CopyTo(frame.AsSpan(LiveV3ProtocolConstants.FrameHeaderSize));
        return frame;
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream)
    {
        var headerBytes = await ReadExactAsync(stream, LiveV3ProtocolConstants.FrameHeaderSize);
        var header = LiveV3ProtocolReader.ParseHeader(headerBytes);
        var frameBytes = new byte[header.TotalFrameLength];
        Buffer.BlockCopy(headerBytes, 0, frameBytes, 0, headerBytes.Length);
        if (header.PayloadLength > 0)
        {
            var payload = await ReadExactAsync(stream, (int)header.PayloadLength);
            Buffer.BlockCopy(payload, 0, frameBytes, LiveV3ProtocolConstants.FrameHeaderSize, payload.Length);
        }

        return frameBytes;
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
}
