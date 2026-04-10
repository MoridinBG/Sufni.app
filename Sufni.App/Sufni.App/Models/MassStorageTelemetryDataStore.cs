using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Extensions;

namespace Sufni.App.Models;

public sealed class MassStorageTelemetryDataStore : ITelemetryDataStore
{
    public string Name { get; }
    public Guid? BoardId { get; }
    public DriveInfo DriveInfo { get; }

    public Task<List<ITelemetryFile>> GetFiles()
    {
        var files = DriveInfo.RootDirectory.GetFiles("*.SST")
            .TrySelect<FileInfo, ITelemetryFile, FormatException>(f => new MassStorageTelemetryFile(f), null)
            .OrderByDescending(f => f.StartTime)
            .ToList();
        return Task.FromResult(files);
    }

    public static async Task<MassStorageTelemetryDataStore> CreateAsync(
        DriveInfo driveInfo,
        CancellationToken cancellationToken = default)
    {
        var rootPath = driveInfo.RootDirectory.FullName;
        var boardIdPath = Path.Combine(rootPath, "BOARDID");
        var uploadedPath = Path.Combine(rootPath, "uploaded");
        var serialHex = (await File.ReadAllTextAsync(boardIdPath, cancellationToken)).ToLowerInvariant();

        Directory.CreateDirectory(uploadedPath);

        return new MassStorageTelemetryDataStore(
            driveInfo,
            UuidUtil.CreateDeviceUuid(serialHex),
            $"{driveInfo.VolumeLabel} ({driveInfo.RootDirectory.Name})");
    }

    private MassStorageTelemetryDataStore(DriveInfo driveInfo, Guid boardId, string name)
    {
        DriveInfo = driveInfo;
        BoardId = boardId;
        Name = name;
    }
}