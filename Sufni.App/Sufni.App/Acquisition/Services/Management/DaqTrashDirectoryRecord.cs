using System.Collections.Generic;

namespace Sufni.App.Acquisition.Services.Management;

public sealed record DaqTrashDirectoryRecord(IReadOnlyList<DaqFileRecord> Files)
    : DaqDirectoryRecord(DaqDirectoryId.Trash, Files);