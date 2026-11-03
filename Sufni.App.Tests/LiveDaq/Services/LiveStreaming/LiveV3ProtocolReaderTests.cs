using System.Buffers.Binary;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveV3ProtocolReaderTests
{
    private const byte SessionId = 42;

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
        var single = LiveV3ProtocolTestFrames.MarkerData();
        byte[] frameBytes = [.. single, single[^1]];
        BinaryPrimitives.WriteUInt32LittleEndian(frameBytes.AsSpan(4, 4), (uint)(frameBytes.Length - 12));
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
