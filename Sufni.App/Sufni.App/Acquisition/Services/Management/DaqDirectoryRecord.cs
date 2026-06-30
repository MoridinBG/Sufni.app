using System.Collections.Generic;

namespace Sufni.App.Acquisition.Services.Management;

public abstract record DaqDirectoryRecord(
    DaqDirectoryId DirectoryId,
    IReadOnlyList<DaqFileRecord> Files);