using System;
using System.Buffers.Binary;
using System.Text;

namespace Sufni.App.Services.Management;

internal sealed class ManagementProtocolReader
{
    private byte[] pendingBytes = [];
    private int pendingOffset;
    private int bufferedByteCount;

    public int BufferedByteCount => bufferedByteCount;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        EnsureWriteCapacity(bytes.Length);
        bytes.CopyTo(pendingBytes.AsSpan(pendingOffset + bufferedByteCount, bytes.Length));
        bufferedByteCount += bytes.Length;
    }

    public void Reset()
    {
        pendingBytes = [];
        pendingOffset = 0;
        bufferedByteCount = 0;
    }

    public bool TryReadFrame(out ManagementProtocolFrame? frame)
    {
        frame = null;
        if (bufferedByteCount < ManagementProtocolConstants.FrameHeaderSize)
        {
            return false;
        }

        var pendingSpan = pendingBytes.AsSpan(pendingOffset, bufferedByteCount);
        var header = ParseHeader(pendingSpan[..ManagementProtocolConstants.FrameHeaderSize]);
        if (bufferedByteCount < header.TotalFrameLength)
        {
            return false;
        }

        frame = ParseFrame(pendingSpan[..header.TotalFrameLength]);
        pendingOffset += header.TotalFrameLength;
        bufferedByteCount -= header.TotalFrameLength;
        if (bufferedByteCount == 0)
        {
            pendingOffset = 0;
        }
        else if (pendingOffset >= pendingBytes.Length / 2)
        {
            CompactUnreadBytes();
        }

        return true;
    }

    public static byte[] CreateListDirectoryRequest(uint requestId, DaqDirectoryId directoryId)
    {
        var payload = new byte[ManagementProtocolConstants.ListDirectoryRequestPayloadSize];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)directoryId);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 0);
        return CreateFrame(ManagementFrameType.ListDirectoryRequest, requestId, payload);
    }

    public static byte[] CreateGetFileRequest(uint requestId, DaqFileClass fileClass, int recordId)
    {
        var payload = new byte[ManagementProtocolConstants.GetFileRequestPayloadSize];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)fileClass);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), recordId);
        return CreateFrame(ManagementFrameType.GetFileRequest, requestId, payload);
    }

    public static byte[] CreateTrashFileRequest(uint requestId, int recordId)
    {
        var payload = new byte[ManagementProtocolConstants.TrashFileRequestPayloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(payload, recordId);
        return CreateFrame(ManagementFrameType.TrashFileRequest, requestId, payload);
    }

    public static byte[] CreatePutFileBeginRequest(uint requestId, DaqFileClass fileClass, ulong fileSizeBytes)
    {
        var payload = new byte[ManagementProtocolConstants.PutFileBeginPayloadSize];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)fileClass);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(4, 8), fileSizeBytes);
        return CreateFrame(ManagementFrameType.PutFileBegin, requestId, payload);
    }

    public static byte[] CreatePutFileChunkFrame(uint requestId, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > ManagementProtocolConstants.MaxPutFileChunkPayloadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), $"PUT_FILE_CHUNK payloads must be <= {ManagementProtocolConstants.MaxPutFileChunkPayloadSize} bytes.");
        }

        return CreateFrame(ManagementFrameType.PutFileChunk, requestId, bytes);
    }

    public static byte[] CreatePutFileCommitRequest(uint requestId) =>
        CreateFrame(ManagementFrameType.PutFileCommit, requestId, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateSetTimeRequest(uint requestId, uint utcSeconds, uint microseconds)
    {
        var payload = new byte[ManagementProtocolConstants.SetTimeRequestPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), utcSeconds);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), microseconds);
        return CreateFrame(ManagementFrameType.SetTimeRequest, requestId, payload);
    }

    public static byte[] CreatePingRequest(uint requestId) =>
        CreateFrame(ManagementFrameType.Ping, requestId, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateFrame(ManagementFrameType frameType, uint requestId, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[ManagementProtocolConstants.FrameHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), ManagementProtocolConstants.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), ManagementProtocolConstants.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), (ushort)frameType);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), requestId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12, 4), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(ManagementProtocolConstants.FrameHeaderSize));
        return frame;
    }

    public static ManagementProtocolFrame ParseFrame(ReadOnlySpan<byte> frameBytes)
    {
        var header = ParseHeader(frameBytes[..ManagementProtocolConstants.FrameHeaderSize]);
        if (frameBytes.Length != header.TotalFrameLength)
        {
            throw new FormatException("Management frame length does not match payload length.");
        }

        var payload = frameBytes[ManagementProtocolConstants.FrameHeaderSize..];
        return header.FrameType switch
        {
            ManagementFrameType.ListDirectoryRequest => ParseListDirectoryRequest(header, payload),
            ManagementFrameType.GetFileRequest => ParseGetFileRequest(header, payload),
            ManagementFrameType.TrashFileRequest => ParseTrashFileRequest(header, payload),
            ManagementFrameType.PutFileBegin => ParsePutFileBeginRequest(header, payload),
            ManagementFrameType.PutFileChunk => new ManagementPutFileChunkFrame(header, payload.ToArray()),
            ManagementFrameType.PutFileCommit => ParseEmptyPayload<ManagementPutFileCommitFrame>(header, payload),
            ManagementFrameType.SetTimeRequest => ParseSetTimeRequest(header, payload),
            ManagementFrameType.Ping => ParseEmptyPayload<ManagementPingFrame>(header, payload),
            ManagementFrameType.ListDirectoryEntry => ParseListDirectoryEntry(header, payload),
            ManagementFrameType.ListDirectoryDone => ParseListDirectoryDone(header, payload),
            ManagementFrameType.FileBegin => ParseFileBegin(header, payload),
            ManagementFrameType.FileChunk => new ManagementFileChunkFrame(header, payload.ToArray()),
            ManagementFrameType.FileEnd => ParseEmptyPayload<ManagementFileEndFrame>(header, payload),
            ManagementFrameType.ActionResult => ParseActionResult(header, payload),
            ManagementFrameType.Error => ParseError(header, payload),
            ManagementFrameType.Pong => ParseEmptyPayload<ManagementPongFrame>(header, payload),
            _ => throw new FormatException($"Unsupported management frame type {(ushort)header.FrameType}.")
        };
    }

    public static ManagementFrameHeader ParseHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < ManagementProtocolConstants.FrameHeaderSize)
        {
            throw new FormatException("Management frame header is truncated.");
        }

        var header = new ManagementFrameHeader(
            Magic: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[0..4]),
            Version: BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[4..6]),
            FrameType: (ManagementFrameType)BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[6..8]),
            RequestId: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[8..12]),
            PayloadLength: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[12..16]));

        if (!header.IsValidMagic)
        {
            throw new FormatException($"Management frame magic 0x{header.Magic:X8} is invalid.");
        }

        if (!header.IsSupportedVersion)
        {
            throw new FormatException($"Management protocol version {header.Version} is not supported.");
        }

        if (header.PayloadLength > ManagementProtocolConstants.MaxPayloadLength)
        {
            throw new FormatException(
                $"Management frame payload length {header.PayloadLength} exceeds maximum {ManagementProtocolConstants.MaxPayloadLength}.");
        }

        return header;
    }

    private void EnsureWriteCapacity(int appendLength)
    {
        var requiredLength = pendingOffset + bufferedByteCount + appendLength;
        if (pendingBytes.Length >= requiredLength)
        {
            return;
        }

        CompactUnreadBytes();
        requiredLength = bufferedByteCount + appendLength;
        if (pendingBytes.Length >= requiredLength)
        {
            return;
        }

        var newLength = pendingBytes.Length == 0 ? 256 : pendingBytes.Length;
        while (newLength < requiredLength)
        {
            newLength = checked(newLength * 2);
        }

        var resized = new byte[newLength];
        if (bufferedByteCount > 0)
        {
            pendingBytes.AsSpan(pendingOffset, bufferedByteCount).CopyTo(resized);
        }

        pendingBytes = resized;
        pendingOffset = 0;
    }

    private void CompactUnreadBytes()
    {
        if (pendingOffset == 0)
        {
            return;
        }

        if (bufferedByteCount == 0)
        {
            pendingOffset = 0;
            return;
        }

        pendingBytes.AsSpan(pendingOffset, bufferedByteCount).CopyTo(pendingBytes);
        pendingOffset = 0;
    }

    private static ManagementListDirectoryRequestFrame ParseListDirectoryRequest(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.ListDirectoryRequestPayloadSize, header.FrameType);
        return new ManagementListDirectoryRequestFrame(header, ParseDirectoryId(BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2])));
    }

    private static ManagementGetFileRequestFrame ParseGetFileRequest(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.GetFileRequestPayloadSize, header.FrameType);
        return new ManagementGetFileRequestFrame(
            header,
            ParseFileClass(BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2])),
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]));
    }

    private static ManagementTrashFileRequestFrame ParseTrashFileRequest(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.TrashFileRequestPayloadSize, header.FrameType);
        return new ManagementTrashFileRequestFrame(header, BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static ManagementPutFileBeginRequestFrame ParsePutFileBeginRequest(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.PutFileBeginPayloadSize, header.FrameType);
        return new ManagementPutFileBeginRequestFrame(
            header,
            ParseFileClass(BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2])),
            BinaryPrimitives.ReadUInt64LittleEndian(payload[4..12]));
    }

    private static ManagementSetTimeRequestFrame ParseSetTimeRequest(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.SetTimeRequestPayloadSize, header.FrameType);
        return new ManagementSetTimeRequestFrame(
            header,
            BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]));
    }

    private static ManagementListDirectoryEntryFrame ParseListDirectoryEntry(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.ListDirectoryEntryPayloadSize, header.FrameType);
        return new ManagementListDirectoryEntryFrame(
            header,
            ParseDirectoryId(BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2])),
            ParseFileClass(BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4])),
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]),
            BinaryPrimitives.ReadInt64LittleEndian(payload[16..24]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[24..28]),
            payload[28],
            ParseAsciiName(payload[29..41]));
    }

    private static ManagementListDirectoryDoneFrame ParseListDirectoryDone(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.ListDirectoryDonePayloadSize, header.FrameType);
        return new ManagementListDirectoryDoneFrame(header, BinaryPrimitives.ReadUInt32LittleEndian(payload));
    }

    private static ManagementFileBeginFrame ParseFileBegin(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.FileBeginPayloadSize, header.FrameType);
        return new ManagementFileBeginFrame(
            header,
            ParseFileClass(BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2])),
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]),
            ParseAsciiName(payload[16..28]));
    }

    private static ManagementActionResultFrame ParseActionResult(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.ActionResultPayloadSize, header.FrameType);
        return new ManagementActionResultFrame(header, BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static ManagementErrorFrame ParseError(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, ManagementProtocolConstants.ErrorPayloadSize, header.FrameType);
        return new ManagementErrorFrame(header, BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static TFrame ParseEmptyPayload<TFrame>(ManagementFrameHeader header, ReadOnlySpan<byte> payload)
        where TFrame : ManagementProtocolFrame
    {
        EnsurePayloadLength(payload, 0, header.FrameType);

        return (TFrame)Activator.CreateInstance(typeof(TFrame), header)!;
    }

    private static void EnsurePayloadLength(ReadOnlySpan<byte> payload, int expectedLength, ManagementFrameType frameType)
    {
        if (payload.Length != expectedLength)
        {
            throw new FormatException($"Management frame {frameType} payload length {payload.Length} is invalid; expected {expectedLength}.");
        }
    }

    private static DaqDirectoryId ParseDirectoryId(ushort rawDirectoryId) => rawDirectoryId switch
    {
        (ushort)DaqDirectoryId.Root => DaqDirectoryId.Root,
        (ushort)DaqDirectoryId.Uploaded => DaqDirectoryId.Uploaded,
        (ushort)DaqDirectoryId.Trash => DaqDirectoryId.Trash,
        _ => throw new FormatException($"Management directory id {rawDirectoryId} is invalid.")
    };

    private static DaqFileClass ParseFileClass(ushort rawFileClass) => rawFileClass switch
    {
        (ushort)DaqFileClass.Config => DaqFileClass.Config,
        (ushort)DaqFileClass.RootSst => DaqFileClass.RootSst,
        (ushort)DaqFileClass.UploadedSst => DaqFileClass.UploadedSst,
        (ushort)DaqFileClass.TrashSst => DaqFileClass.TrashSst,
        _ => throw new FormatException($"Management file class {rawFileClass} is invalid.")
    };

    private static string ParseAsciiName(ReadOnlySpan<byte> bytes)
    {
        var terminatorIndex = bytes.IndexOf((byte)0);
        var effectiveLength = terminatorIndex >= 0 ? terminatorIndex : bytes.Length;
        return Encoding.ASCII.GetString(bytes[..effectiveLength]);
    }
}