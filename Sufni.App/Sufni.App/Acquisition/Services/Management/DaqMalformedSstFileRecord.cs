using System;

namespace Sufni.App.Acquisition.Services.Management;

public sealed record DaqMalformedSstFileRecord(
    DaqFileClass FileClass,
    string Name,
    ulong FileSizeBytes,
    int RecordId,
    DateTimeOffset? TimestampUtc,
    TimeSpan? Duration,
    byte SstVersion,
    string MalformedMessage)
    : DaqFileRecord(FileClass, Name, FileSizeBytes);