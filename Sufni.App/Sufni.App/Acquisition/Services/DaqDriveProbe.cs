using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Acquisition.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Extensions;

namespace Sufni.App.Acquisition.Services;

public sealed class DaqDriveProbe : IDaqDriveProbe
{
    public IReadOnlyList<DaqDrive> FindDrives() => DriveInfo.GetDrives()
        .Where(drive => drive.IsReady && File.Exists(Path.Combine(drive.RootDirectory.FullName, DaqDriveLayout.BoardIdFileName)))
        .Select(drive => new DaqDrive(
            drive.RootDirectory.FullName,
            drive.VolumeLabel,
            $"{drive.VolumeLabel} ({drive.RootDirectory.Name})"))
        .ToArray();

    public async Task<Guid> InitializeAsync(DaqDrive drive, CancellationToken cancellationToken)
    {
        var serialHex = (await File.ReadAllTextAsync(Path.Combine(drive.RootPath, DaqDriveLayout.BoardIdFileName), cancellationToken))
            .ToLowerInvariant();
        Directory.CreateDirectory(Path.Combine(drive.RootPath, DaqDriveLayout.UploadedDirectoryName));
        return UuidUtil.CreateDeviceUuid(serialHex);
    }

    public IReadOnlyList<ITelemetryFile> GetFiles(DaqDrive drive) =>
        new DirectoryInfo(drive.RootPath).GetFiles("*.SST")
            .TrySelect<FileInfo, ITelemetryFile, Exception>(file => new MassStorageTelemetryFile(file), null)
            .ToArray();
}
