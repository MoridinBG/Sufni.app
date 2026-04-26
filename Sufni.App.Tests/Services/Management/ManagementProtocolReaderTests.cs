using System.Buffers.Binary;
using Sufni.App.Services.Management;

namespace Sufni.App.Tests.Services.Management;

public class ManagementProtocolReaderTests
{
    [Fact]
    public void TryReadFrame_WaitsForCompleteHeaderAndPayloadAcrossReads()
    {
        var reader = new ManagementProtocolReader();
        var frameBytes = ManagementProtocolTestFrames.CreateListDirectoryEntryFrame(requestId: 5);

        reader.Append(frameBytes.AsSpan(0, 10));
        Assert.False(reader.TryReadFrame(out var frame));
        Assert.Null(frame);

        reader.Append(frameBytes.AsSpan(10, 12));
        Assert.False(reader.TryReadFrame(out frame));
        Assert.Null(frame);

        reader.Append(frameBytes.AsSpan(22));
        Assert.True(reader.TryReadFrame(out frame));

        var entry = Assert.IsType<ManagementListDirectoryEntryFrame>(frame);
        Assert.Equal((uint)5, entry.RequestId);
        Assert.Equal("00001.SST", entry.Name);
        Assert.Equal(0, reader.BufferedByteCount);
    }

    [Fact]
    public void TryReadFrame_ThrowsWhenHeaderMagicIsInvalid()
    {
        var reader = new ManagementProtocolReader();
        var frameBytes = ManagementProtocolTestFrames.CreateListDirectoryDoneFrame(requestId: 1, entryCount: 0);
        frameBytes[0] = 0;

        reader.Append(frameBytes);

        Assert.Throws<FormatException>(() => reader.TryReadFrame(out _));
    }

    [Fact]
    public void TryReadFrame_ThrowsWhenHeaderVersionIsInvalid()
    {
        var reader = new ManagementProtocolReader();
        var frameBytes = ManagementProtocolTestFrames.CreateListDirectoryDoneFrame(requestId: 1, entryCount: 0);
        BinaryPrimitives.WriteUInt16LittleEndian(frameBytes.AsSpan(4, 2), 1);

        reader.Append(frameBytes);

        Assert.Throws<FormatException>(() => reader.TryReadFrame(out _));
    }

    [Fact]
    public void ParseFrame_ThrowsWhenFixedPayloadLengthIsInvalid()
    {
        var invalidFrame = ManagementProtocolReader.CreateFrame(
            ManagementFrameType.ListDirectoryDone,
            requestId: 2,
            payload: new byte[3]);

        Assert.Throws<FormatException>(() => ManagementProtocolReader.ParseFrame(invalidFrame));
    }

    [Fact]
    public void ParseFrame_ReturnsListDirectoryEntryFrame_WithTypedValues()
    {
        var frameBytes = ManagementProtocolTestFrames.CreateListDirectoryEntryFrame(
            requestId: 7,
            directoryId: DaqDirectoryId.Trash,
            fileClass: DaqFileClass.TrashSst,
            recordId: 88,
            fileSizeBytes: 4321,
            timestampUtcSeconds: 1_700_000_123,
            durationMilliseconds: 9876,
            sstVersion: 4,
            name: "00088.SST");

        var frame = Assert.IsType<ManagementListDirectoryEntryFrame>(ManagementProtocolReader.ParseFrame(frameBytes));

        Assert.Equal((uint)7, frame.RequestId);
        Assert.Equal(DaqDirectoryId.Trash, frame.DirectoryId);
        Assert.Equal(DaqFileClass.TrashSst, frame.FileClass);
        Assert.Equal(88, frame.RecordId);
        Assert.Equal((ulong)4321, frame.FileSizeBytes);
        Assert.Equal(1_700_000_123, frame.TimestampUtcSeconds);
        Assert.Equal((uint)9876, frame.DurationMilliseconds);
        Assert.Equal((byte)4, frame.SstVersion);
        Assert.Equal("00088.SST", frame.Name);
    }

    [Fact]
    public void ParseFrame_ReturnsFileBeginFrame_WithExpectedValues()
    {
        var frameBytes = ManagementProtocolTestFrames.CreateFileBeginFrame(
            requestId: 9,
            fileClass: DaqFileClass.Config,
            recordId: 0,
            fileSizeBytes: 77,
            maxChunkPayload: 8192,
            name: "CONFIG");

        var frame = Assert.IsType<ManagementFileBeginFrame>(ManagementProtocolReader.ParseFrame(frameBytes));

        Assert.Equal((uint)9, frame.RequestId);
        Assert.Equal(DaqFileClass.Config, frame.FileClass);
        Assert.Equal(0, frame.RecordId);
        Assert.Equal((ulong)77, frame.FileSizeBytes);
        Assert.Equal((uint)8192, frame.MaxChunkPayload);
        Assert.Equal("CONFIG", frame.Name);
    }

    [Fact]
    public void CreateRequestHelpers_RoundTripToTypedRequestFrames()
    {
        var list = Assert.IsType<ManagementListDirectoryRequestFrame>(
            ManagementProtocolReader.ParseFrame(
                ManagementProtocolReader.CreateListDirectoryRequest(1, DaqDirectoryId.Uploaded)));
        Assert.Equal(DaqDirectoryId.Uploaded, list.DirectoryId);

        var get = Assert.IsType<ManagementGetFileRequestFrame>(
            ManagementProtocolReader.ParseFrame(
                ManagementProtocolReader.CreateGetFileRequest(2, DaqFileClass.TrashSst, 42)));
        Assert.Equal(DaqFileClass.TrashSst, get.FileClass);
        Assert.Equal(42, get.RecordId);

        var trash = Assert.IsType<ManagementTrashFileRequestFrame>(
            ManagementProtocolReader.ParseFrame(
                ManagementProtocolReader.CreateTrashFileRequest(3, 99)));
        Assert.Equal(99, trash.RecordId);

        var uploaded = Assert.IsType<ManagementMarkSstUploadedRequestFrame>(
            ManagementProtocolReader.ParseFrame(
                ManagementProtocolReader.CreateMarkSstUploadedRequest(7, 88)));
        Assert.Equal(88, uploaded.RecordId);

        var putBegin = Assert.IsType<ManagementPutFileBeginRequestFrame>(
            ManagementProtocolReader.ParseFrame(
                ManagementProtocolReader.CreatePutFileBeginRequest(4, DaqFileClass.Config, 128)));
        Assert.Equal(DaqFileClass.Config, putBegin.FileClass);
        Assert.Equal((ulong)128, putBegin.FileSizeBytes);

        var setTime = Assert.IsType<ManagementSetTimeRequestFrame>(
            ManagementProtocolReader.ParseFrame(
                ManagementProtocolReader.CreateSetTimeRequest(5, 123, 456)));
        Assert.Equal((uint)123, setTime.UtcSeconds);
        Assert.Equal((uint)456, setTime.Microseconds);

        Assert.IsType<ManagementPingFrame>(
            ManagementProtocolReader.ParseFrame(ManagementProtocolReader.CreatePingRequest(6)));
    }

    [Fact]
    public void CreatePutFileChunkFrame_RejectsPayloadsAboveLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagementProtocolReader.CreatePutFileChunkFrame(1, new byte[513]));
    }

    [Fact]
    public void TryReadFrame_ThrowsWhenPayloadLengthExceedsMaximum()
    {
        var reader = new ManagementProtocolReader();
        var header = new byte[ManagementProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), ManagementProtocolConstants.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), ManagementProtocolConstants.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), (ushort)ManagementFrameType.ListDirectoryDone);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(12, 4),
            (uint)ManagementProtocolConstants.MaxPayloadLength + 1);

        reader.Append(header);

        Assert.Throws<FormatException>(() => reader.TryReadFrame(out _));
    }

    [Fact]
    public void ParseHeader_ThrowsWhenPayloadLengthExceedsMaximum()
    {
        var header = new byte[ManagementProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), ManagementProtocolConstants.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), ManagementProtocolConstants.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), (ushort)ManagementFrameType.ListDirectoryDone);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(12, 4),
            (uint)ManagementProtocolConstants.MaxPayloadLength + 1);

        Assert.Throws<FormatException>(() => ManagementProtocolReader.ParseHeader(header));
    }
}