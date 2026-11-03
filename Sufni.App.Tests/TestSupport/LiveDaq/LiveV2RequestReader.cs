using System.Buffers.Binary;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal static class LiveV2RequestReader
{
    public static LiveProtocolFrame ParseFrame(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16 || !bytes[..4].SequenceEqual("LIVE"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..6]) != 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..12]) != 0)
            throw new FormatException("Expected an empty live v2 request.");
        var metadata = new LiveFrameMetadata(BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..16]));
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..8]) switch
        {
            2 => new LiveStopRequestFrame(metadata),
            4 => new LiveIdentifyRequestFrame(metadata),
            _ => throw new FormatException("Unexpected live v2 request.")
        };
    }
}

internal sealed record LiveIdentifyRequestFrame(LiveFrameMetadata Header) : LiveProtocolFrame(Header);
