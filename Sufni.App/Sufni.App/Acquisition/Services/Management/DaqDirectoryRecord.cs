using System.Collections.Generic;

namespace Sufni.App.Acquisition.Services.Management;

public closed record DaqDirectoryRecord(
    DaqDirectoryId DirectoryId,
    IReadOnlyList<DaqFileRecord> Files);