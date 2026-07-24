using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.App.LiveDaq.Services;
#endif
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Common;
using Sufni.App.Tests.TestSupport.LiveDaq;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqV3ClientTests
{
    private const byte SessionId = 42;

    [Fact]
    public async Task ConnectAsync_SendsHandshakeBeforeAnyFrame()
    {
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV3Client>(() => new LiveDaqV3Client());

        await harness.WithServerAsync(
            async stream =>
            {
                var handshake = await LiveDaqClientProtocolHarness<LiveDaqV3Client>.ReadExactAsync(
                    stream,
                    LiveV3ProtocolConstants.HandshakeSize);
                Assert.Equal(LiveV3ProtocolReader.CreateHandshake(), handshake);
                await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
                await stream.FlushAsync();
            },
            async (client, port) =>
            {
                await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
                return true;
            });
    }

    [Fact]
    public async Task ConnectAsync_EnablesNoDelayBeforeSendingHandshake()
    {
        TcpClient? createdTcpClient = null;
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV3Client>(() =>
            new LiveDaqV3Client(
                TimeSpan.FromSeconds(1),
                expectedBoardId: null,
                tcpClientFactory: () => createdTcpClient = new TcpClient(),
                sendBytesAsync: async (stream, bytes, cancellationToken) =>
                {
                    Assert.True(createdTcpClient!.NoDelay);
                    await stream.WriteAsync(bytes, cancellationToken);
                }));

        await harness.WithServerAsync(
            async stream =>
            {
                _ = await LiveDaqClientProtocolHarness<LiveDaqV3Client>.ReadExactAsync(
                    stream,
                    LiveV3ProtocolConstants.HandshakeSize);
                await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
                await stream.FlushAsync();
            },
            async (client, port) =>
            {
                await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
                return true;
            });
    }

    [Theory]
    [InlineData(ConnectFailureKind.InvalidServerHello)]
    [InlineData(ConnectFailureKind.MismatchedDiscoveredBoardId)]
    public async Task ConnectAsync_RejectsInvalidHello(ConnectFailureKind failureKind)
    {
        var expectedBoardId = UuidUtil.CreateDeviceUuid("0123456789abcdef").ToString();
        var harness = new LiveDaqClientProtocolHarness<LiveDaqV3Client>(() =>
            failureKind is ConnectFailureKind.MismatchedDiscoveredBoardId
                ? new LiveDaqV3Client(expectedBoardId)
                : new LiveDaqV3Client());

        await harness.WithServerAsync(
            async stream =>
            {
                _ = await LiveDaqClientProtocolHarness<LiveDaqV3Client>.ReadExactAsync(
                    stream,
                    LiveV3ProtocolConstants.HandshakeSize);
                var hello = LiveV3ProtocolTestFrames.ServerHello();
                if (failureKind is ConnectFailureKind.InvalidServerHello)
                {
                    hello[4] = 4;
                }
                else
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(hello.AsSpan(12, 8), 0x1112131415161718UL);
                }

                await stream.WriteAsync(hello);
                await stream.FlushAsync();
            },
            async (client, port) =>
            {
                if (failureKind is ConnectFailureKind.InvalidServerHello)
                {
                    await Assert.ThrowsAsync<FormatException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), port));
                }
                else
                {
                    await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync(IPAddress.Loopback.ToString(), port));
                }

                return true;
            });
    }

    [Fact]
    public async Task Transport_AcceptsFragmentedHelloHeadersPayloadsAndCoalescedFrames()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            Assert.Equal(
                LiveV3ProtocolTestFrames.Handshake(),
                await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
            await WriteInChunksAsync(
                stream,
                LiveV3ProtocolTestFrames.ServerHello(),
                [1, 2, 3, 5, 8]);

            Assert.IsType<LiveV3CapabilitiesRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            await WriteInChunksAsync(
                stream,
                LiveV3ProtocolTestFrames.CapabilitiesResponse(),
                [3, 1, 11, 2, 17]);

            Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            byte[] coalesced =
            [
                .. LiveV3ProtocolTestFrames.StartResultPending(),
                .. LiveV3ProtocolTestFrames.SessionHeaderTravel(),
                .. LiveV3ProtocolTestFrames.TravelData(),
            ];
            await stream.WriteAsync(coalesced);
            await stream.FlushAsync();

            _ = await ReadFrameAsync(stream);
        });

        await using var client = new LiveDaqV3Client();
        var travelObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveTravelBatchFrame })
            {
                travelObserved.TrySetResult();
            }
        });
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(
                    new LiveStartRequest(LiveSensorInstanceMask.Travel, 200_000, 0, 0))
                .WaitAsync(TimeSpan.FromSeconds(2)));
        await travelObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await client.DisconnectAsync();
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
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
            await stream.FlushAsync();

            var capabilitiesRequest = Assert.IsType<LiveV3CapabilitiesRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
            Assert.Equal((uint)0, capabilitiesRequest.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.CapabilitiesResponse());
            await stream.FlushAsync();

            var startRequest = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
            Assert.Equal((uint)1, startRequest.Sequence);
            Assert.Equal(
                [SstV5ProtocolConstants.StreamTravel, SstV5ProtocolConstants.StreamImu, SstV5ProtocolConstants.StreamGps],
                startRequest.Payload.StreamRequests.Select(record => record.StreamKind).ToArray());
            Assert.DoesNotContain(startRequest.Payload.StreamRequests, record => record.StreamKind is SstV5ProtocolConstants.StreamBattery or SstV5ProtocolConstants.StreamMarker);
            Assert.Equal(LiveSensorInstanceMask.Travel, startRequest.Payload.StreamRequests[0].SourceMask);
            Assert.Equal((uint)200_000, startRequest.Payload.StreamRequests[0].RateMhz);
            Assert.Equal((uint)0, startRequest.Payload.StreamRequests[0].BatchDurationMs);
            Assert.Equal(
                LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
                startRequest.Payload.StreamRequests[1].SourceMask);
            Assert.Equal((uint)200_000, startRequest.Payload.StreamRequests[1].RateMhz);
            Assert.Equal(LiveSensorInstanceMask.Gps, startRequest.Payload.StreamRequests[2].SourceMask);
            Assert.Equal((uint)5_000, startRequest.Payload.StreamRequests[2].RateMhz);
            Assert.Equal((uint)0, startRequest.Payload.StreamRequests[2].ExtensionMask);

            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
            await stream.FlushAsync();
            startResultSent.TrySetResult();

            await allowSessionHeader.Task;
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderTravelImuGps());
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var startTask = client.StartPreviewAsync(
            new LiveStartRequest(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps, 200_000, 200_000, 5_000));

        await startResultSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(startTask.IsCompleted);
        allowSessionHeader.TrySetResult();

        var result = await startTask.WaitAsync(TimeSpan.FromSeconds(2));
        var started = Assert.IsType<LivePreviewStartResult.Started>(result);
        Assert.Equal(SessionId, started.Header.SessionId);
        Assert.Equal(LiveProtocolVersion.V3, started.Header.ProtocolVersion);
        Assert.Equal((uint)200_000, started.Header.AcceptedTravelRateMhz);
        Assert.Equal((uint)200_000, started.Header.AcceptedImuRateMhz);
        Assert.Equal((uint)5_000, started.Header.AcceptedGpsRateMhz);
        Assert.Equal(
            LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps,
            started.Header.RequestedSensorMask);
        Assert.Equal(
            LiveStreamMask.Travel | LiveStreamMask.Imu | LiveStreamMask.Gps,
            started.Header.RequestedStreamMask);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

#if SUFNI_PROFILING_DIAGNOSTICS
    [Fact]
    public async Task StartPreviewAsync_ExpandsReplayRequestFromCapabilities_AndAcceptsMatchingHeader()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            Assert.Equal(
                LiveV3ProtocolReader.CreateHandshake(),
                await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello(
                uniqueBoardId: 0x5934dcc01ee18f70UL));
            await stream.FlushAsync();

            Assert.IsType<LiveV3CapabilitiesRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.CapabilitiesResponse());
            await stream.FlushAsync();

            var start = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(
                [
                    SstV5ProtocolConstants.StreamTravel,
                    SstV5ProtocolConstants.StreamImu,
                    SstV5ProtocolConstants.StreamTemperature,
                    SstV5ProtocolConstants.StreamGps,
                    SstV5ProtocolConstants.StreamBattery,
                    SstV5ProtocolConstants.StreamMarker,
                ],
                start.Payload.StreamRequests.Select(record => record.StreamKind).ToArray());
            Assert.All(start.Payload.StreamRequests, record =>
            {
                Assert.Equal(0, record.RecordFlags);
                Assert.Equal(0u, record.RateMhz);
                Assert.Equal(0u, record.BatchDurationMs);
            });
            Assert.Equal(
                SstV5ProtocolConstants.ExtensionGpsDiagPublicV1,
                start.Payload.StreamRequests.Single(
                    record => record.StreamKind == SstV5ProtocolConstants.StreamGps).ExtensionMask);

            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderAllStreams());
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultAllStreams());
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client(ProfilingLiveDaqReplay.BoardId);
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var started = Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(new LiveStartRequest(
                    RequestedSensorMask: LiveSensorInstanceMask.Travel,
                    TravelRateMhz: 200_000,
                    ImuRateMhz: 0,
                    GpsRateMhz: 0,
                    RequestedStreamMask: LiveStreamMask.Travel))
                .WaitAsync(TimeSpan.FromSeconds(2)));

        const LiveStreamMask allStreams =
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps |
            LiveStreamMask.Battery |
            LiveStreamMask.Marker;
        Assert.Equal(allStreams, started.Header.RequestedStreamMask);
        Assert.Equal(allStreams, started.Header.AcceptedStreamMask);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }
#endif

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
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
            await stream.FlushAsync();

            var request = Assert.IsType<LiveV3DeviceStateRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
            Assert.Equal((uint)0, request.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.DeviceStateResponse());
            await stream.FlushAsync();
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var state = await client.RequestDeviceStateAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SstV5ProtocolConstants.StreamTravel, state.Streams[0].StreamKind);
        Assert.True(state.Sources[0].Available);

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
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
            await stream.FlushAsync();
            _ = await ReadFrameAsync(stream);
            await allowResponse.Task;
            await stream.WriteAsync(LiveV3ProtocolTestFrames.DeviceStateResponse());
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
    public async Task RequestDeviceStateAsync_ReturnsResponse_DuringActiveSession()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartedServerAsync(
            listener,
            () => LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            async stream =>
            {
                var requestBytes = await ReadFrameAsync(stream);
                var request = Assert.IsType<LiveV3DeviceStateRequestFrame>(
                    LiveV3ProtocolReader.ParseFrame(
                        requestBytes,
                        new LiveV3SessionDecodeContext()));
                Assert.Equal(2u, request.Sequence);
                await stream.WriteAsync(LiveV3ProtocolTestFrames.DeviceStateResponse());
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(
                    new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
                .WaitAsync(TimeSpan.FromSeconds(2)));

        var state = await client.RequestDeviceStateAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SstV5ProtocolConstants.StreamTravel, state.Streams[0].StreamKind);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PingAsync_CompletesOnlyWhenPongNonceMatches(bool mismatch)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var allowServerClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const uint nonce = 0xa1b2c3d4;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            _ = await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
            await stream.FlushAsync();

            var ping = Assert.IsType<LivePingFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(nonce, ping.Nonce);
            var pong = LiveV3ProtocolTestFrames.Pong(sessionId: 0, sequence: 4);
            if (mismatch)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    pong.AsSpan(LiveV3ProtocolConstants.FrameHeaderSize, sizeof(uint)),
                    nonce + 1);
            }
            await stream.WriteAsync(pong);
            await stream.FlushAsync();
            await allowServerClose.Task;
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        if (mismatch)
        {
            await Assert.ThrowsAsync<IOException>(() => client.PingAsync(nonce));
        }
        else
        {
            await client.PingAsync(nonce).WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.True(client.IsConnected);

        allowServerClose.TrySetResult();
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
                await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultDenied());
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = Assert.IsType<LivePreviewStartResult.Rejected>(result);
        Assert.Equal(LiveStartErrorCode.InvalidRequest, rejected.ErrorCode);
        Assert.Equal(2, rejected.AdmissionReasons.Count);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PriorityConflictDenial_KeepsConnectionOpen()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var allowServerClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            Assert.Equal(
                LiveV3ProtocolTestFrames.Handshake(),
                await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
            await stream.FlushAsync();

            Assert.Equal(
                LiveV3ProtocolTestFrames.CapabilitiesRequest(),
                await ReadFrameAsync(stream));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.CapabilitiesResponse());
            await stream.FlushAsync();

            var expectedStart = LiveV3ProtocolTestFrames.StartRequestAllStreams();
            var actualStart = await ReadFrameAsync(stream);
            Assert.Equal(expectedStart[..8], actualStart[..8]);
            Assert.Equal(expectedStart[12..], actualStart[12..]);
            Assert.Equal(1u, LiveV3ProtocolReader.ParseHeader(actualStart).TxSequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPriorityConflict());
            await stream.FlushAsync();
            await allowServerClose.Task;
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var result = await client.StartPreviewAsync(CreateAllStreamStartRequest())
            .WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = Assert.IsType<LivePreviewStartResult.Rejected>(result);
        Assert.Equal(LiveStartErrorCode.Busy, rejected.ErrorCode);
        Assert.Single(rejected.AdmissionReasons);
        Assert.True(client.IsConnected);

        allowServerClose.TrySetResult();
        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StartPreviewAsync_BatteryOnlyDenialIsAdmissionResult_NotProtocolFailure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var allowServerClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            _ = await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
            _ = await ReadFrameAsync(stream);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.CapabilitiesResponse());
            await stream.FlushAsync();

            var start = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            var battery = Assert.Single(start.Payload.StreamRequests);
            Assert.Equal(SstV5ProtocolConstants.StreamBattery, battery.StreamKind);
            Assert.Equal(LiveSensorInstanceMask.Battery, battery.SourceMask);
            Assert.Equal(0, battery.RecordFlags);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultDenied());
            await stream.FlushAsync();
            await allowServerClose.Task;
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var result = await client.StartPreviewAsync(new LiveStartRequest(
                RequestedSensorMask: LiveSensorInstanceMask.Battery,
                TravelRateMhz: 0,
                ImuRateMhz: 0,
                GpsRateMhz: 0,
                RequestedStreamMask: LiveStreamMask.Battery))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<LivePreviewStartResult.Rejected>(result);
        Assert.True(client.IsConnected);

        allowServerClose.TrySetResult();
        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TemperatureOnlyStart_IsAcceptedAndEmitsTemperatureData()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartupServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderTemperature());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.TemperatureData());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultTemperature());
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        var temperatureObserved = new TaskCompletionSource<LiveTemperatureBatchFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveTemperatureBatchFrame frame })
            {
                temperatureObserved.TrySetResult(frame);
            }
        });
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var started = Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(new LiveStartRequest(
                    RequestedSensorMask: LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
                    TravelRateMhz: 0,
                    ImuRateMhz: 0,
                    GpsRateMhz: 0,
                    RequestedStreamMask: LiveStreamMask.Temperature,
                    TemperatureRateMhz: 30))
                .WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(LiveStreamMask.Temperature, started.Header.AcceptedStreamMask);
        Assert.Equal(30u, started.Header.AcceptedTemperatureRateMhz);
        Assert.Equal(2, (await temperatureObserved.Task.WaitAsync(TimeSpan.FromSeconds(2))).Records.Count);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PriorityTelemetryBatteryAndMarkerStart_IsAcceptedWithGpsExtensionOmission()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartupServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderAllStreams());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.BatteryData());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.MarkerData());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultAllStreams());
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        var batteryObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var markerObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveBatteryBatchFrame })
            {
                batteryObserved.TrySetResult();
            }
            else if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveMarkerBatchFrame })
            {
                markerObserved.TrySetResult();
            }
        });
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var started = Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(CreateAllStreamStartRequest())
                .WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps |
            LiveStreamMask.Battery |
            LiveStreamMask.Marker,
            started.Header.AcceptedStreamMask);
        var omission = Assert.Single(started.Header.AdmissionOmissions);
        Assert.Equal(SstV5ProtocolConstants.StreamGps, omission.StreamKind);
        Assert.Equal(SstV5ProtocolConstants.ExtensionGpsDiagPublicV1, omission.TargetMask);
        await batteryObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await markerObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PartialAcceptance_PreservesRequestedStreamsAndImuOmission()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartupServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderPartialTravel());
                await stream.FlushAsync();
                _ = await ReadFrameAsync(stream);
            });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        var started = Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(new LiveStartRequest(
                    LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu,
                    200_000,
                    200_000,
                    0))
                .WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(LiveStreamMask.Travel | LiveStreamMask.Imu, started.Header.RequestedStreamMask);
        Assert.Equal(LiveStreamMask.Travel, started.Header.AcceptedStreamMask);
        var omission = Assert.Single(started.Header.AdmissionOmissions);
        Assert.Equal(SstV5ProtocolConstants.StreamImu, omission.StreamKind);
        Assert.Equal(LiveV3ProtocolHelpers.AdmissionUnavailable, omission.Reason);

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
                await stream.WriteAsync(LiveV3ProtocolTestFrames.Error());
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
    public async Task MalformedKnownFrame_FailsPendingStartAndClosesConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartupServerAsync(
            listener,
            async stream =>
            {
                await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
                var malformedHeader = LiveV3ProtocolTestFrames.SessionHeaderTravel();
                malformedHeader[13] = 2;
                await stream.WriteAsync(malformedHeader);
                await stream.FlushAsync();
                var closedBuffer = new byte[1];
                Assert.Equal(
                    0,
                    await stream.ReadAsync(closedBuffer).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            });

        await using var client = new LiveDaqV3Client();
        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.Faulted)
            {
                faulted.TrySetResult();
            }
        });
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);

        Assert.IsType<LivePreviewStartResult.Failed>(
            await client.StartPreviewAsync(
                    new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
                .WaitAsync(TimeSpan.FromSeconds(2)));
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(client.IsConnected);

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
                await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending(sessionId: 43, sequence: 1));
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultPreheaderFailure());
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
    public async Task PreheaderSessionFailure_LeavesConnectionReadyForAnotherStart()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            await ReadHandshakeCapabilitiesAndStartAsync(stream);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending(sessionId: 43, sequence: 1));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultPreheaderFailure());
            await stream.FlushAsync();

            var retry = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(2u, retry.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderTravel());
            await stream.FlushAsync();

            var disconnectStop = await ReadFrameAsync(stream);
            Assert.Equal(SessionId, LiveV3ProtocolReader.ParseHeader(disconnectStop).SessionId);
        });

        await using var client = new LiveDaqV3Client();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var request = new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0);

        Assert.IsType<LivePreviewStartResult.Failed>(
            await client.StartPreviewAsync(request).WaitAsync(TimeSpan.FromSeconds(2)));
        var retry = Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(request).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal((uint)SessionId, retry.Header.SessionId);

        await client.DisconnectAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Events_EmitSinkGapStatusBeforeAffectedDataAndTerminal()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartedServerAsync(
            listener,
            () => LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            async stream =>
            {
                await stream.WriteAsync(LiveV3ProtocolTestFrames.StatusTravelGap());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.TravelData());
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultTravel());
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client();
        var observed = new List<Type>();
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
        var result = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Started>(result);

        await framesObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        lock (observed)
        {
            Assert.True(
                observed.IndexOf(typeof(LiveStatusFrame)) <
                observed.IndexOf(typeof(LiveTravelBatchFrame)));
            Assert.True(
                observed.IndexOf(typeof(LiveTravelBatchFrame)) <
                observed.IndexOf(typeof(LiveSessionResultFrame)));
        }

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
            () => LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            async stream =>
            {
                var unknown = LiveV3ProtocolTestFrames.Pong();
                unknown[1] = 0xee;
                await stream.WriteAsync(unknown);
                await stream.WriteAsync(LiveV3ProtocolTestFrames.TravelData());
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
    public async Task TerminalControl_RemainsAvailable_WhenTelemetryQueueHasNoCapacity()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var allowServerClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            await ReadHandshakeCapabilitiesAndStartAsync(stream);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderTravel());
            for (uint index = 0; index < 64; index++)
            {
                await stream.WriteAsync(LiveV3ProtocolTestFrames.TravelData());
            }
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultTravel());
            await stream.FlushAsync();

            var secondStart = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(2u, secondStart.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultDenied());
            await stream.FlushAsync();
            await allowServerClose.Task;
        });

        await using var client = new LiveDaqV3Client(
            TimeSpan.FromSeconds(2),
            expectedBoardId: null,
            tcpClientFactory: static () => new TcpClient(),
            sendBytesAsync: static async (networkStream, bytes, cancellationToken) =>
            {
                await networkStream.WriteAsync(bytes, cancellationToken);
                await networkStream.FlushAsync(cancellationToken);
            },
            rawFrameCapacity: 0,
            parsedFrameCapacity: 0);
        var terminalObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.FrameReceived { Frame: LiveSessionResultFrame })
            {
                terminalObserved.TrySetResult();
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var request = new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0);
        Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(request).WaitAsync(TimeSpan.FromSeconds(2)));
        await terminalObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Rejected>(
            await client.StartPreviewAsync(request).WaitAsync(TimeSpan.FromSeconds(2)));

        allowServerClose.TrySetResult();
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
            () => LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            async stream =>
            {
                var stopRequest = Assert.IsType<LiveStopRequestFrame>(
                    LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
                Assert.Equal((uint)2, stopRequest.Sequence);

                await stream.WriteAsync(LiveV3ProtocolTestFrames.StopResult());
                await stream.FlushAsync();
                stopResultSent.TrySetResult();

                await allowSessionResult.Task;
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultTravel());
                await stream.FlushAsync();
            });

        await using var client = new LiveDaqV3Client(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
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
    public async Task StopPreviewAsync_RejectsTerminalResultBeforeStopResult()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunStartedServerAsync(
            listener,
            () => LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            async stream =>
            {
                _ = Assert.IsType<LiveStopRequestFrame>(
                    LiveV3ProtocolReader.ParseFrame(
                        await ReadFrameAsync(stream),
                        new LiveV3SessionDecodeContext()));
                await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultTravel());
                await stream.FlushAsync();
                var closedBuffer = new byte[1];
                Assert.Equal(
                    0,
                    await stream.ReadAsync(closedBuffer).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            });

        await using var client = new LiveDaqV3Client(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(
                    new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
                .WaitAsync(TimeSpan.FromSeconds(2)));

        await Assert.ThrowsAsync<IOException>(() => client.StopPreviewAsync());
        Assert.False(client.IsConnected);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Connection_AllowsDeniedStartThenSequentialSessionsAndSessionIdWrap()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            Assert.Equal(
                LiveV3ProtocolReader.CreateHandshake(),
                await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
            await stream.FlushAsync();

            var capabilitiesRequest = Assert.IsType<LiveV3CapabilitiesRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(0u, capabilitiesRequest.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.CapabilitiesResponse());

            var deniedStart = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(1u, deniedStart.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultDenied());
            await stream.FlushAsync();

            var firstAcceptedStart = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(2u, firstAcceptedStart.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending(sessionId: 255, sequence: 2));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderTravel(sessionId: 255, sequence: 9));
            await stream.FlushAsync();

            var firstStopBytes = await ReadFrameAsync(stream);
            Assert.Equal(255, LiveV3ProtocolReader.ParseHeader(firstStopBytes).SessionId);
            var firstStop = Assert.IsType<LiveStopRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    firstStopBytes,
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(3u, firstStop.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StopResult(sessionId: 255, sequence: 13));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionResultTravel(sessionId: 255, sequence: 12));
            await stream.FlushAsync();

            var wrappedStart = Assert.IsType<LiveV3StartRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    await ReadFrameAsync(stream),
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(4u, wrappedStart.Sequence);
            await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending(sessionId: 1, sequence: 0));
            await stream.WriteAsync(LiveV3ProtocolTestFrames.SessionHeaderTravel(sessionId: 1, sequence: 8));
            await stream.FlushAsync();

            var disconnectStopBytes = await ReadFrameAsync(stream);
            Assert.Equal(1, LiveV3ProtocolReader.ParseHeader(disconnectStopBytes).SessionId);
            var disconnectStop = Assert.IsType<LiveStopRequestFrame>(
                LiveV3ProtocolReader.ParseFrame(
                    disconnectStopBytes,
                    new LiveV3SessionDecodeContext()));
            Assert.Equal(5u, disconnectStop.Sequence);
        });

        await using var client = new LiveDaqV3Client(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var request = new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0);

        Assert.IsType<LivePreviewStartResult.Rejected>(
            await client.StartPreviewAsync(request).WaitAsync(TimeSpan.FromSeconds(2)));
        var firstStarted = Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(request).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(255u, firstStarted.Header.SessionId);

        await client.StopPreviewAsync().WaitAsync(TimeSpan.FromSeconds(2));

        var wrappedStarted = Assert.IsType<LivePreviewStartResult.Started>(
            await client.StartPreviewAsync(request).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1u, wrappedStarted.Header.SessionId);

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
            () => LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            async stream =>
            {
                await stream.WriteAsync(LiveV3ProtocolTestFrames.Error());
                await stream.FlushAsync();
                var closedBuffer = new byte[1];
                Assert.Equal(
                    0,
                    await stream.ReadAsync(closedBuffer).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            });

        await using var client = new LiveDaqV3Client();
        var faulted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.Faulted faultedEvent)
            {
                faulted.TrySetResult(faultedEvent.ErrorMessage);
            }
            else if (clientEvent is LiveDaqClientEvent.Disconnected)
            {
                disconnected.TrySetResult();
            }
        });

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var started = await client.StartPreviewAsync(new LiveStartRequest(LiveSensorInstanceMask.Travel, 100_000, 0, 0))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<LivePreviewStartResult.Started>(started);

        var errorMessage = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains("error 2", errorMessage);
        Assert.Contains("frame type 99", errorMessage);
        Assert.Contains("3737844653", errorMessage);
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(client.IsConnected);

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
        Func<byte[]> createSessionHeader,
        Func<NetworkStream, Task> afterStartedAsync)
    {
        using var serverClient = await listener.AcceptTcpClientAsync();
        await using var stream = serverClient.GetStream();
        await ReadHandshakeCapabilitiesAndStartAsync(stream);
        await stream.WriteAsync(LiveV3ProtocolTestFrames.StartResultPending());
        await stream.WriteAsync(createSessionHeader());
        await stream.FlushAsync();
        await afterStartedAsync(stream);
    }

    private static async Task ReadHandshakeCapabilitiesAndStartAsync(NetworkStream stream)
    {
        Assert.Equal(LiveV3ProtocolReader.CreateHandshake(), await ReadExactAsync(stream, LiveV3ProtocolConstants.HandshakeSize));
        await stream.WriteAsync(LiveV3ProtocolTestFrames.ServerHello());
        await stream.FlushAsync();
        Assert.IsType<LiveV3CapabilitiesRequestFrame>(
            LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
        await stream.WriteAsync(LiveV3ProtocolTestFrames.CapabilitiesResponse());
        await stream.FlushAsync();
        Assert.IsType<LiveV3StartRequestFrame>(
            LiveV3ProtocolReader.ParseFrame(await ReadFrameAsync(stream), new LiveV3SessionDecodeContext()));
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

    private static LiveStartRequest CreateAllStreamStartRequest() => new(
        RequestedSensorMask: LiveSensorInstanceMask.All,
        TravelRateMhz: 200_000,
        ImuRateMhz: 200_000,
        GpsRateMhz: 5_000,
        RequestedStreamMask: LiveStreamMask.Travel |
                             LiveStreamMask.Imu |
                             LiveStreamMask.Temperature |
                             LiveStreamMask.Gps |
                             LiveStreamMask.Battery |
                             LiveStreamMask.Marker,
        TemperatureRateMhz: 30,
        TravelBatchDurationMs: 50,
        ImuBatchDurationMs: 50,
        RequestGpsDiagnostics: true,
        Priority: true,
        NoGpsHeaderWait: true);

    private static async Task WriteInChunksAsync(
        NetworkStream stream,
        byte[] bytes,
        int[] chunkSizes)
    {
        var offset = 0;
        var chunkIndex = 0;
        while (offset < bytes.Length)
        {
            var requested = chunkSizes[chunkIndex % chunkSizes.Length];
            var length = Math.Min(requested, bytes.Length - offset);
            await stream.WriteAsync(bytes.AsMemory(offset, length));
            await stream.FlushAsync();
            offset += length;
            chunkIndex++;
        }
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

    public enum ConnectFailureKind
    {
        InvalidServerHello,
        MismatchedDiscoveredBoardId
    }
}
