using Sufni.App.Shared.IO;
using System.Buffers.Binary;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;

namespace Sufni.App.Tests.Acquisition.Services.Management;

internal sealed class ManagementRequestReader
{
    private readonly FramedMessageReader reader = new(
        16,
        static bytes => checked(16 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..])));

    public void Append(ReadOnlySpan<byte> bytes) => reader.Append(bytes);

    public bool TryReadFrame(out ManagementProtocolFrame? frame) =>
        reader.TryReadFrame(ParseFrame, out frame);

    private static ManagementProtocolFrame ParseFrame(ReadOnlySpan<byte> bytes)
    {
        Assert.Equal("MGMT"u8.ToArray(), bytes[..4].ToArray());
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]));
        var header = new ManagementFrameHeader(0x544D474D, 2,
            (ManagementFrameType)BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]));
        var payload = bytes[16..];
        return (ushort)header.FrameType switch
        {
            1 => new ManagementListDirectoryRequestFrame(
                header, (DaqDirectoryId)BinaryPrimitives.ReadUInt16LittleEndian(payload)),
            2 => new ManagementGetFileRequestFrame(
                header,
                (DaqFileClass)BinaryPrimitives.ReadUInt16LittleEndian(payload),
                BinaryPrimitives.ReadInt32LittleEndian(payload[4..])),
            3 => new ManagementTrashFileRequestFrame(
                header, BinaryPrimitives.ReadInt32LittleEndian(payload)),
            9 => new ManagementMarkSstUploadedRequestFrame(
                header, BinaryPrimitives.ReadInt32LittleEndian(payload)),
            4 => new ManagementPutFileBeginRequestFrame(
                header,
                (DaqFileClass)BinaryPrimitives.ReadUInt16LittleEndian(payload),
                BinaryPrimitives.ReadUInt64LittleEndian(payload[4..])),
            5 => new ManagementPutFileChunkFrame(header, payload.ToArray()),
            6 => new ManagementPutFileCommitFrame(header),
            7 => new ManagementSetTimeRequestFrame(
                header,
                BinaryPrimitives.ReadUInt32LittleEndian(payload),
                BinaryPrimitives.ReadUInt32LittleEndian(payload[4..])),
            8 => new ManagementPingFrame(header),
            _ => throw new FormatException($"Unexpected management request {header.FrameType}.")
        };
    }
}
