using System.Collections.Generic;

namespace Sufni.App.Services.Management;

public sealed record DaqUploadedDirectoryRecord(IReadOnlyList<DaqFileRecord> Files)
    : DaqDirectoryRecord(DaqDirectoryId.Uploaded, Files);