using System.Collections.Generic;

namespace Sufni.App.Acquisition.Services.Management;

public sealed record DaqRootDirectoryRecord(IReadOnlyList<DaqFileRecord> Files)
    : DaqDirectoryRecord(DaqDirectoryId.Root, Files);