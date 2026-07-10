using System.Buffers.Binary;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveV3ProtocolReaderTests
{
    private const byte SessionId = 42;

    [Fact]
    public void CreateHandshake_WritesExpectedBytes()
    {
        Assert.Equal(
            LiveV3ProtocolTestFrames.Handshake(),
            LiveV3ProtocolReader.CreateHandshake());
    }

    [Fact]
    public void ParseServerHello_ParsesVersionFeaturesAndBoardIdentity()
    {
        var hello = LiveV3ProtocolReader.ParseServerHello(
            LiveV3ProtocolTestFrames.ServerHello());

        Assert.Equal((byte)0, hello.ProtoMinor);
        Assert.Equal((ushort)0, hello.FeatureFlags);
        Assert.Equal((uint)LiveV3ProtocolConstants.MaxPayloadLength, hello.MaxFramePayloadBytes);
        Assert.Equal((ulong)0x0123456789abcdef, hello.UniqueBoardId);
    }

    [Fact]
    public void ParseServerHello_RejectsInvalidMagicMajorAndZeroBoardId()
    {
        var invalidMagic = LiveV3ProtocolTestFrames.ServerHello();
        invalidMagic[0] = (byte)'X';
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(invalidMagic));

        var invalidMajor = LiveV3ProtocolTestFrames.ServerHello();
        invalidMajor[4] = 4;
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(invalidMajor));

        var zeroBoard = LiveV3ProtocolTestFrames.ServerHello();
        Array.Clear(zeroBoard, 12, 8);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(zeroBoard));

        byte[] longHello = [.. LiveV3ProtocolTestFrames.ServerHello(), 0];
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(longHello));
    }

    [Fact]
    public void ParseHeader_Parses12ByteLayout()
    {
        var frameBytes = LiveV3ProtocolTestFrames.Pong();

        var header = LiveV3ProtocolReader.ParseHeader(frameBytes);

        Assert.Equal(LiveV3FrameType.Pong, header.FrameType);
        Assert.Equal(SessionId, header.SessionId);
        Assert.Equal(0, header.FrameFlags);
        Assert.Equal((uint)4, header.PayloadLength);
        Assert.Equal(frameBytes.Length, header.TotalFrameLength);
    }

    [Fact]
    public void ParseHeader_RejectsUnknownFrameType()
    {
        var header = LiveV3ProtocolTestFrames.Pong();
        header[1] = 0xff;

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseHeader(header));
    }

    [Fact]
    public void ParseHeader_AllowsUnknownFrameType_WhenRequested()
    {
        var headerBytes = LiveV3ProtocolTestFrames.Pong();
        headerBytes[1] = 0xfe;

        var header = LiveV3ProtocolReader.ParseHeader(headerBytes, allowUnknownFrameType: true);

        Assert.Equal((LiveV3FrameType)0xFE, header.FrameType);
        Assert.Equal((uint)4, header.PayloadLength);
    }

    [Fact]
    public void ParseHeader_RejectsNonzeroFlags()
    {
        var header = LiveV3ProtocolTestFrames.Pong();
        header[2] = 1;

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseHeader(header));
    }

    [Fact]
    public void ParseHeader_RejectsPayloadLengthOverMaximum()
    {
        var header = LiveV3ProtocolTestFrames.Pong();
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(4, 4),
            LiveV3ProtocolConstants.MaxPayloadLength + 1);

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseHeader(header));
    }

    [Fact]
    public void ParseFrame_CapabilitiesResponse_ParsesStreamRecords()
    {
        var frame = Assert.IsType<LiveV3CapabilitiesFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.CapabilitiesResponse(),
                new LiveV3SessionDecodeContext()));

        Assert.Equal((uint)LiveV3ProtocolConstants.MaxPayloadLength, frame.Payload.MaxFramePayloadBytes);
        Assert.Equal(6, frame.Payload.Streams.Count);
        var stream = frame.Payload.Streams[0];
        Assert.Equal(LiveStreamMask.Travel, stream.Stream);
        Assert.Equal(LiveSensorInstanceMask.Travel, stream.SupportedSourceMask);
        Assert.Equal((uint)1_000, stream.MinRateMhz);
        Assert.Equal((uint)1_000_000, stream.MaxRateMhz);
    }

    [Fact]
    public void ParseFrame_StartResult_PendingStoresSessionId()
    {
        var context = new LiveV3SessionDecodeContext();

        var frame = Assert.IsType<LiveV3StartResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.StartResultPending(),
                context));

        Assert.True(frame.Payload.IsPending);
        Assert.Equal(SessionId, frame.Payload.SessionId);
        Assert.Equal(SessionId, context.AcceptedSessionId);
    }

    [Fact]
    public void ParseFrame_StartResult_DeniedParsesAdmissionReasons()
    {
        var frame = Assert.IsType<LiveV3StartResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.StartResultDenied(),
                new LiveV3SessionDecodeContext()));

        Assert.True(frame.Payload.IsDenied);
        var reason = frame.Payload.AdmissionReasons[1];
        Assert.Equal(LiveStreamMask.Imu, reason.Stream);
        Assert.Equal(LiveSensorInstanceMask.RearImu, reason.Sources);
        Assert.Equal((byte)2, reason.TargetKind);
        Assert.Equal((uint)LiveSensorInstanceMask.RearImu, reason.TargetMask);
    }

    [Fact]
    public void ParseFrame_SessionHeader_ParsesDescriptorsIntoCanonicalHeaderAndContext()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(SessionId);
        var frame = Assert.IsType<LiveSessionHeaderFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.SessionHeaderAllStreams(),
                context));

        Assert.Equal(SessionId, frame.Payload.SessionId);
        Assert.Equal(LiveProtocolVersion.V3, frame.Payload.ProtocolVersion);
        Assert.Equal((uint)200_000, frame.Payload.AcceptedTravelRateMhz);
        Assert.Equal((uint)200_000, frame.Payload.AcceptedImuRateMhz);
        Assert.Equal((uint)5_000, frame.Payload.AcceptedGpsRateMhz);
        Assert.Equal(LiveImuLocationMask.Frame | LiveImuLocationMask.Fork, frame.Payload.ActiveImuMask);
        Assert.Equal(LiveSensorInstanceMask.None, frame.Payload.RequestedSensorMask);
        Assert.Equal(
            LiveSensorInstanceMask.Travel |
            LiveSensorInstanceMask.FrameImu |
            LiveSensorInstanceMask.ForkImu |
            LiveSensorInstanceMask.Gps |
            LiveSensorInstanceMask.Battery,
            frame.Payload.AcceptedSensorMask);
        Assert.Equal(6, context.StreamDescriptors.Count);
        Assert.True(context.TryGetStream(SstV5ProtocolConstants.StreamTravel, out _));
    }

    [Fact]
    public void ParseFrame_DataBeforeSessionHeader_Fails()
    {
        var frameBytes = LiveV3ProtocolTestFrames.TravelData();

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(frameBytes, new LiveV3SessionDecodeContext()));
    }

    [Fact]
    public void ParseFrame_MarkerDataWithMultipleSamples_Fails()
    {
        var context = CreateAllStreamContext();
        var frameBytes = LiveV3ProtocolTestFrames.MarkerData();
        BinaryPrimitives.WriteUInt32LittleEndian(frameBytes.AsSpan(28, 4), 2);

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(frameBytes, context));
    }

    [Fact]
    public void ParseFrame_TemperatureDataWithoutAcceptedDescriptor_Fails()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(SessionId);
        _ = LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            context);
        var frameBytes = LiveV3ProtocolTestFrames.TemperatureData();

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(frameBytes, context));
    }

    [Fact]
    public void ParseFrame_StatusAndSessionResult_ParseCounters()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(SessionId);
        _ = LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolTestFrames.SessionHeaderTravel(),
            context);

        var statusFrame = Assert.IsType<LiveStatusFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.StatusTravelGap(),
                context));
        var status = statusFrame.Streams[0];
        Assert.Single(statusFrame.Streams);
        Assert.Equal(LiveStreamMask.Travel, status.Stream);
        Assert.Equal((byte)1, status.ProducerState);
        Assert.Equal((byte)0, status.ProducerFailureReason);
        Assert.Equal((ushort)3, status.SinkBacklogBatches);
        Assert.Equal((ulong)2, status.SinkMissedCount);

        var resultFrame = Assert.IsType<LiveSessionResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.SessionResultTravel(),
                context));
        Assert.Equal(SessionId, resultFrame.Payload.SessionId);
        Assert.Equal((byte)1, resultFrame.Payload.FinalStatus.SessionResultReason);
        Assert.Equal((ulong)50_000, resultFrame.Payload.FinalStatus.StoppedMonotonicDeltaUs);
        Assert.Single(resultFrame.Payload.FinalStatus.Streams);
        Assert.Equal(SstV5ProtocolConstants.StreamTravel, resultFrame.Payload.FinalStatus.Streams[0].StreamKind);
        Assert.Equal((ushort)3, resultFrame.Payload.FinalStatus.Streams[0].SinkBacklogBatches);
    }

    [Fact]
    public void ParseFrame_DeviceStateResponse_ParsesStreamAndSourceState()
    {
        var frame = Assert.IsType<LiveV3DeviceStateFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.DeviceStateResponse(),
                new LiveV3SessionDecodeContext()));

        var stream = frame.Payload.Streams[0];
        Assert.Equal(SstV5ProtocolConstants.StreamTravel, stream.StreamKind);
        Assert.Equal((uint)200_000, stream.EffectiveDefaultRateMhz);
        Assert.Equal((uint)50, stream.DefaultBatchDurationMs);
        var source = frame.Payload.Sources[0];
        Assert.Equal((byte)2, source.CalibrationStatus);
        Assert.True(source.Available);
        Assert.Equal(LiveSensorInstanceMask.ForkTravel, source.Source);
    }

    [Fact]
    public void ParseFrame_DeviceStateFrames_RequireSessionIdZero()
    {
        var request = LiveV3ProtocolTestFrames.DeviceStateRequest();
        request[0] = SessionId;
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(
            request,
            new LiveV3SessionDecodeContext()));

        var response = LiveV3ProtocolTestFrames.DeviceStateResponse();
        response[0] = SessionId;
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(
            response,
            new LiveV3SessionDecodeContext()));
    }

    [Fact]
    public void ParseFrame_TxSequenceDiscontinuity_IsTolerated()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(SessionId);
        var firstBytes = LiveV3ProtocolTestFrames.Pong();
        var secondBytes = LiveV3ProtocolTestFrames.Pong();
        BinaryPrimitives.WriteUInt32LittleEndian(firstBytes.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(secondBytes.AsSpan(8, 4), 3);

        var first = LiveV3ProtocolReader.ParseFrame(firstBytes, context);
        var second = LiveV3ProtocolReader.ParseFrame(secondBytes, context);

        Assert.Equal((uint)1, first.Sequence);
        Assert.Equal((uint)3, second.Sequence);
    }

    private static LiveV3SessionDecodeContext CreateAllStreamContext()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(SessionId);
        _ = LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolTestFrames.SessionHeaderAllStreams(),
            context);
        return context;
    }
}
