using System;
using System.Buffers.Binary;
using System.Text;
using Sufni.App.Services.Management;

namespace Sufni.App.Tests.Services.Management;

internal static class ManagementProtocolTestFrames
{
    public static byte[] CreateListDirectoryEntryFrame(
        uint requestId,
        DaqDirectoryId directoryId = DaqDirectoryId.Root,
        DaqFileClass fileClass = DaqFileClass.RootSst,
        int recordId = 1,
        ulong fileSizeBytes = 1234,
        long timestampUtcSeconds = 1_700_000_000,
        uint durationMilliseconds = 6000,
        byte sstVersion = 4,
        string name = "00001.SST")
    {
        var payload = new byte[ManagementProtocolConstants.ListDirectoryEntryPayloadSize];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)directoryId);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), (ushort)fileClass);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), recordId);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), fileSizeBytes);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(16, 8), timestampUtcSeconds);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), durationMilliseconds);
        payload[28] = sstVersion;
        WriteName(payload.AsSpan(29, 12), name);
        return ManagementProtocolReader.CreateFrame(ManagementFrameType.ListDirectoryEntry, requestId, payload);
    }

    public static byte[] CreateListDirectoryDoneFrame(uint requestId, uint entryCount)
    {
        var payload = new byte[ManagementProtocolConstants.ListDirectoryDonePayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, entryCount);
        return ManagementProtocolReader.CreateFrame(ManagementFrameType.ListDirectoryDone, requestId, payload);
    }

    public static byte[] CreateFileBeginFrame(
        uint requestId,
        DaqFileClass fileClass = DaqFileClass.RootSst,
        int recordId = 1,
        ulong fileSizeBytes = 4,
        string name = "00001.SST")
    {
        var payload = new byte[ManagementProtocolConstants.FileBeginPayloadSize];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)fileClass);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), recordId);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), fileSizeBytes);
        WriteName(payload.AsSpan(16, 12), name);
        return ManagementProtocolReader.CreateFrame(ManagementFrameType.FileBegin, requestId, payload);
    }

    public static byte[] CreateFileChunkFrame(uint requestId, byte[] bytes) =>
        ManagementProtocolReader.CreateFrame(ManagementFrameType.FileChunk, requestId, bytes);

    public static byte[] CreateFileEndFrame(uint requestId) =>
        ManagementProtocolReader.CreateFrame(ManagementFrameType.FileEnd, requestId, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateActionResultFrame(uint requestId, int resultCode)
    {
        var payload = new byte[ManagementProtocolConstants.ActionResultPayloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(payload, resultCode);
        return ManagementProtocolReader.CreateFrame(ManagementFrameType.ActionResult, requestId, payload);
    }

    public static byte[] CreateErrorFrame(uint requestId, int errorCode)
    {
        var payload = new byte[ManagementProtocolConstants.ErrorPayloadSize];
        BinaryPrimitives.WriteInt32LittleEndian(payload, errorCode);
        return ManagementProtocolReader.CreateFrame(ManagementFrameType.Error, requestId, payload);
    }

    public static byte[] CreatePongFrame(uint requestId) =>
        ManagementProtocolReader.CreateFrame(ManagementFrameType.Pong, requestId, ReadOnlySpan<byte>.Empty);

    private static void WriteName(Span<byte> destination, string name)
    {
        destination.Clear();
        var nameBytes = Encoding.ASCII.GetBytes(name);
        if (nameBytes.Length > destination.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(name), $"Management test names must be <= {destination.Length} bytes.");
        }

        nameBytes.CopyTo(destination);
    }
}