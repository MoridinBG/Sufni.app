using System;
using System.Buffers.Binary;
using System.Text;

using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Tests.Acquisition.Services.Management;

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
        var payload = new byte[41];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), directoryId switch { DaqDirectoryId.Root => (ushort)1, DaqDirectoryId.Uploaded => (ushort)2, DaqDirectoryId.Trash => (ushort)3, _ => throw new ArgumentOutOfRangeException(nameof(directoryId)) });
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), fileClass switch { DaqFileClass.Config => (ushort)1, DaqFileClass.RootSst => (ushort)2, DaqFileClass.UploadedSst => (ushort)3, DaqFileClass.TrashSst => (ushort)4, _ => throw new ArgumentOutOfRangeException(nameof(fileClass)) });
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), recordId);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), fileSizeBytes);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(16, 8), timestampUtcSeconds);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), durationMilliseconds);
        payload[28] = sstVersion;
        WriteName(payload.AsSpan(29, 12), name);
        return CreateFrame(16, requestId, payload);
    }

    public static byte[] CreateListDirectoryDoneFrame(uint requestId, uint entryCount)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, entryCount);
        return CreateFrame(17, requestId, payload);
    }

    public static byte[] CreateFileBeginFrame(
        uint requestId,
        DaqFileClass fileClass = DaqFileClass.RootSst,
        int recordId = 1,
        ulong fileSizeBytes = 4,
        uint maxChunkPayload = 4096,
        string name = "00001.SST")
    {
        var payload = new byte[32];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), fileClass switch { DaqFileClass.Config => (ushort)1, DaqFileClass.RootSst => (ushort)2, DaqFileClass.UploadedSst => (ushort)3, DaqFileClass.TrashSst => (ushort)4, _ => throw new ArgumentOutOfRangeException(nameof(fileClass)) });
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), recordId);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), fileSizeBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), maxChunkPayload);
        WriteName(payload.AsSpan(20, 12), name);
        return CreateFrame(18, requestId, payload);
    }

    public static byte[] CreateFileChunkFrame(uint requestId, byte[] bytes) =>
        CreateFrame(19, requestId, bytes);

    public static byte[] CreateFileEndFrame(uint requestId) =>
        CreateFrame(20, requestId, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateActionResultFrame(uint requestId, int resultCode)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, resultCode);
        return CreateFrame(21, requestId, payload);
    }

    public static byte[] CreateErrorFrame(uint requestId, int errorCode)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, errorCode);
        return CreateFrame(22, requestId, payload);
    }

    public static byte[] CreatePongFrame(uint requestId) =>
        CreateFrame(23, requestId, ReadOnlySpan<byte>.Empty);

    // Firmware MGMT v2 envelope; deliberately independent of the production encoder.
    private static byte[] CreateFrame(ushort type, uint requestId, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[16 + payload.Length];
        "MGMT"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), requestId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(bytes.AsSpan(16));
        return bytes;
    }

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