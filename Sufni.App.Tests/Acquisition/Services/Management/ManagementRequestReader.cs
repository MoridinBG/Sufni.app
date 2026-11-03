using Sufni.App.Shared.IO;
using System.Buffers.Binary;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;

namespace Sufni.App.Tests.Acquisition.Services.Management;

internal sealed class ManagementRequestReader
{
    private readonly FramedMessageReader reader = new(
        ManagementProtocolConstants.FrameHeaderSize,
        static bytes => ManagementProtocolReader.ParseHeader(bytes).TotalFrameLength);

    public void Append(ReadOnlySpan<byte> bytes) => reader.Append(bytes);

    public bool TryReadFrame(out ManagementProtocolFrame? frame) =>
        reader.TryReadFrame(ParseFrame, out frame);

    private static ManagementProtocolFrame ParseFrame(ReadOnlySpan<byte> bytes)
    {
        var header = ManagementProtocolReader.ParseHeader(bytes);
        var payload = bytes[ManagementProtocolConstants.FrameHeaderSize..];
        return header.FrameType switch
        {
            ManagementFrameType.ListDirectoryRequest => new ManagementListDirectoryRequestFrame(
                header, (DaqDirectoryId)BinaryPrimitives.ReadUInt16LittleEndian(payload)),
            ManagementFrameType.GetFileRequest => new ManagementGetFileRequestFrame(
                header,
                (DaqFileClass)BinaryPrimitives.ReadUInt16LittleEndian(payload),
                BinaryPrimitives.ReadInt32LittleEndian(payload[4..])),
            ManagementFrameType.TrashFileRequest => new ManagementTrashFileRequestFrame(
                header, BinaryPrimitives.ReadInt32LittleEndian(payload)),
            ManagementFrameType.MarkSstUploadedRequest => new ManagementMarkSstUploadedRequestFrame(
                header, BinaryPrimitives.ReadInt32LittleEndian(payload)),
            ManagementFrameType.PutFileBegin => new ManagementPutFileBeginRequestFrame(
                header,
                (DaqFileClass)BinaryPrimitives.ReadUInt16LittleEndian(payload),
                BinaryPrimitives.ReadUInt64LittleEndian(payload[4..])),
            ManagementFrameType.PutFileChunk => new ManagementPutFileChunkFrame(header, payload.ToArray()),
            ManagementFrameType.PutFileCommit => new ManagementPutFileCommitFrame(header),
            ManagementFrameType.SetTimeRequest => new ManagementSetTimeRequestFrame(
                header,
                BinaryPrimitives.ReadUInt32LittleEndian(payload),
                BinaryPrimitives.ReadUInt32LittleEndian(payload[4..])),
            ManagementFrameType.Ping => new ManagementPingFrame(header),
            _ => throw new FormatException($"Unexpected management request {header.FrameType}.")
        };
    }
}
