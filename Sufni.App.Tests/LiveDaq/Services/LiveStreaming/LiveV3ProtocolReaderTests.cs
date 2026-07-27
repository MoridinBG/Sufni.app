using System.Buffers.Binary;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveV3ProtocolReaderTests
{
    private const byte SessionId = 42;

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
