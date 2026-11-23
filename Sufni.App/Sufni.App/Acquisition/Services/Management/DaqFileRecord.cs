namespace Sufni.App.Acquisition.Services.Management;

public closed record DaqFileRecord(
    DaqFileClass FileClass,
    string Name,
    ulong FileSizeBytes);