using Sufni.App.Acquisition.Services.Management;

namespace Sufni.App.Tests.Acquisition.Services.Management;

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
