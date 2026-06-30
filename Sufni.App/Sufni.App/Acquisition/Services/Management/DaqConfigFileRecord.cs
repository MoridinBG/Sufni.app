namespace Sufni.App.Acquisition.Services.Management;

public sealed record DaqConfigFileRecord(string Name, ulong FileSizeBytes)
    : DaqFileRecord(DaqFileClass.Config, Name, FileSizeBytes);