namespace Sufni.App.Services.Management;

public abstract record DaqFileRecord(
    DaqFileClass FileClass,
    string Name,
    ulong FileSizeBytes);