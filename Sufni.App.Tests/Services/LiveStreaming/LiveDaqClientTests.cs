using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;

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
}