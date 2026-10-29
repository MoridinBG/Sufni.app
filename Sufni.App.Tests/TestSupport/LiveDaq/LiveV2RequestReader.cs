using System.Buffers.Binary;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal static class LiveV2RequestReader
{
    public static LiveProtocolFrame ParseFrame(ReadOnlySpan<byte> bytes)
    {
        var header = LiveV2ProtocolReader.ParseHeader(bytes);
        var metadata = new LiveFrameMetadata(header.Sequence);
        var payload = bytes[LiveV2ProtocolConstants.FrameHeaderSize..];
        return header.FrameType switch
        {
            LiveV2FrameType.StartLive => new LiveStartRequestFrame(metadata, new LiveStartRequest(
                (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload),
                BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]) * 1000,
                BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]) * 1000,
                BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]) * 1000)),
            LiveV2FrameType.StopLive => new LiveStopRequestFrame(metadata),
            LiveV2FrameType.Ping => new LivePingFrame(metadata),
            LiveV2FrameType.Identify => new LiveIdentifyRequestFrame(metadata),
            _ => throw new FormatException($"Unexpected live v2 request {header.FrameType}.")
        };
    }
}

internal sealed record LiveIdentifyRequestFrame(LiveFrameMetadata Header) : LiveProtocolFrame(Header);
