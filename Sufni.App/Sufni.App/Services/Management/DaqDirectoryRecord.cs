using System.Collections.Generic;

namespace Sufni.App.Services.Management;

public abstract record DaqDirectoryRecord(
    DaqDirectoryId DirectoryId,
    IReadOnlyList<DaqFileRecord> Files);