using System;

namespace Sufni.App.Services.Management;

internal static class ManagementProtocolConstants
{
    public const uint Magic = 0x544D474D;
    public const ushort Version = 2;

    public const int FrameHeaderSize = 16;

    public const int ListDirectoryRequestPayloadSize = 4;
    public const int GetFileRequestPayloadSize = 8;
    public const int TrashFileRequestPayloadSize = 4;
    public const int MarkSstUploadedRequestPayloadSize = 4;
    public const int PutFileBeginPayloadSize = 12;
    public const int PutFileCommitPayloadSize = 0;
    public const int SetTimeRequestPayloadSize = 8;
    public const int PingPayloadSize = 0;

    public const int ListDirectoryEntryPayloadSize = 41;
    public const int ListDirectoryDonePayloadSize = 4;
    public const int FileBeginPayloadSize = 32;
    public const int FileEndPayloadSize = 0;
    public const int ActionResultPayloadSize = 4;
    public const int ErrorPayloadSize = 4;
    public const int PongPayloadSize = 0;

    public const int MaxPutFileChunkPayloadSize = 512;

    public const int MaxPayloadLength = 1024 * 1024;
}

internal enum ManagementFrameType : ushort
{
    ListDirectoryRequest = 1,
    GetFileRequest = 2,
    TrashFileRequest = 3,
    PutFileBegin = 4,
    PutFileChunk = 5,
    PutFileCommit = 6,
    SetTimeRequest = 7,
    Ping = 8,
    MarkSstUploadedRequest = 9,

    ListDirectoryEntry = 16,
    ListDirectoryDone = 17,
    FileBegin = 18,
    FileChunk = 19,
    FileEnd = 20,
    ActionResult = 21,
    Error = 22,
    Pong = 23,
}

internal enum ManagementResultCode : int
{
    Ok = 0,
    InvalidRequest = -1,
    NotFound = -2,
    Busy = -3,
    IoError = -4,
    ValidationError = -5,
    UnsupportedTarget = -6,
    InternalError = -7,
}

internal readonly record struct ManagementFrameHeader(
    uint Magic,
    ushort Version,
    ManagementFrameType FrameType,
    uint RequestId,
    uint PayloadLength)
{
    public bool IsValidMagic => Magic == ManagementProtocolConstants.Magic;

    public bool IsSupportedVersion => Version == ManagementProtocolConstants.Version;

    public int TotalFrameLength => checked(ManagementProtocolConstants.FrameHeaderSize + (int)PayloadLength);
}

internal abstract record ManagementProtocolFrame(ManagementFrameHeader Header)
{
    public uint RequestId => Header.RequestId;
}

internal sealed record ManagementListDirectoryRequestFrame(
    ManagementFrameHeader Header,
    DaqDirectoryId DirectoryId) : ManagementProtocolFrame(Header);

internal sealed record ManagementGetFileRequestFrame(
    ManagementFrameHeader Header,
    DaqFileClass FileClass,
    int RecordId) : ManagementProtocolFrame(Header);

internal sealed record ManagementTrashFileRequestFrame(
    ManagementFrameHeader Header,
    int RecordId) : ManagementProtocolFrame(Header);

internal sealed record ManagementMarkSstUploadedRequestFrame(
    ManagementFrameHeader Header,
    int RecordId) : ManagementProtocolFrame(Header);

internal sealed record ManagementPutFileBeginRequestFrame(
    ManagementFrameHeader Header,
    DaqFileClass FileClass,
    ulong FileSizeBytes) : ManagementProtocolFrame(Header);

internal sealed record ManagementPutFileChunkFrame(
    ManagementFrameHeader Header,
    byte[] Bytes) : ManagementProtocolFrame(Header);

internal sealed record ManagementPutFileCommitFrame(ManagementFrameHeader Header) : ManagementProtocolFrame(Header);

internal sealed record ManagementSetTimeRequestFrame(
    ManagementFrameHeader Header,
    uint UtcSeconds,
    uint Microseconds) : ManagementProtocolFrame(Header);

internal sealed record ManagementPingFrame(ManagementFrameHeader Header) : ManagementProtocolFrame(Header);

internal sealed record ManagementListDirectoryEntryFrame(
    ManagementFrameHeader Header,
    DaqDirectoryId DirectoryId,
    DaqFileClass FileClass,
    int RecordId,
    ulong FileSizeBytes,
    long TimestampUtcSeconds,
    uint DurationMilliseconds,
    byte SstVersion,
    string Name) : ManagementProtocolFrame(Header);

internal sealed record ManagementListDirectoryDoneFrame(
    ManagementFrameHeader Header,
    uint EntryCount) : ManagementProtocolFrame(Header);

internal sealed record ManagementFileBeginFrame(
    ManagementFrameHeader Header,
    DaqFileClass FileClass,
    int RecordId,
    ulong FileSizeBytes,
    uint MaxChunkPayload,
    string Name) : ManagementProtocolFrame(Header);

internal sealed record ManagementFileChunkFrame(
    ManagementFrameHeader Header,
    byte[] Bytes) : ManagementProtocolFrame(Header);

internal sealed record ManagementFileEndFrame(ManagementFrameHeader Header) : ManagementProtocolFrame(Header);

internal sealed record ManagementActionResultFrame(
    ManagementFrameHeader Header,
    int ResultCode) : ManagementProtocolFrame(Header);

internal sealed record ManagementErrorFrame(
    ManagementFrameHeader Header,
    int ErrorCode) : ManagementProtocolFrame(Header);

internal sealed record ManagementPongFrame(ManagementFrameHeader Header) : ManagementProtocolFrame(Header);

internal static class ManagementProtocolHelpers
{
    public static bool TryMapErrorCode(int rawErrorCode, out DaqManagementErrorCode errorCode)
    {
        switch (rawErrorCode)
        {
            case (int)ManagementResultCode.InvalidRequest:
                errorCode = DaqManagementErrorCode.InvalidRequest;
                return true;
            case (int)ManagementResultCode.NotFound:
                errorCode = DaqManagementErrorCode.NotFound;
                return true;
            case (int)ManagementResultCode.Busy:
                errorCode = DaqManagementErrorCode.Busy;
                return true;
            case (int)ManagementResultCode.IoError:
                errorCode = DaqManagementErrorCode.IoError;
                return true;
            case (int)ManagementResultCode.ValidationError:
                errorCode = DaqManagementErrorCode.ValidationError;
                return true;
            case (int)ManagementResultCode.UnsupportedTarget:
                errorCode = DaqManagementErrorCode.UnsupportedTarget;
                return true;
            case (int)ManagementResultCode.InternalError:
                errorCode = DaqManagementErrorCode.InternalError;
                return true;
            default:
                errorCode = default;
                return false;
        }
    }

    public static string ToUserMessage(DaqManagementErrorCode errorCode) => errorCode switch
    {
        DaqManagementErrorCode.InvalidRequest => "The device rejected the management request.",
        DaqManagementErrorCode.NotFound => "The requested file or directory was not found on the device.",
        DaqManagementErrorCode.Busy => "The device is busy and cannot process the management request right now.",
        DaqManagementErrorCode.IoError => "The device reported an I/O error while processing the management request.",
        DaqManagementErrorCode.ValidationError => "The device rejected the uploaded CONFIG file during validation.",
        DaqManagementErrorCode.UnsupportedTarget => "The requested target is not supported by the device.",
        DaqManagementErrorCode.InternalError => "The device reported an internal management error.",
        _ => $"The device returned management error code {(int)errorCode}."
    };
}