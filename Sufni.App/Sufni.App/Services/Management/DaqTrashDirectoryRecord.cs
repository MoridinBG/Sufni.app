using System.Collections.Generic;

namespace Sufni.App.Services.Management;

public sealed record DaqTrashDirectoryRecord(IReadOnlyList<DaqFileRecord> Files)
    : DaqDirectoryRecord(DaqDirectoryId.Trash, Files);