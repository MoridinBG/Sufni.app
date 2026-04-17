using System;

namespace Sufni.App.Services.Management;

public sealed record DaqSstFileRecord(
    DaqFileClass FileClass,
    string Name,
    ulong FileSizeBytes,
    int RecordId,
    DateTimeOffset TimestampUtc,
    TimeSpan Duration,
    byte SstVersion)
    : DaqFileRecord(FileClass, Name, FileSizeBytes);