using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Extensions;

namespace Sufni.App.Models;

public class MassStorageTelemetryDataStore : ITelemetryDataStore
{
    public string Name { get; }
    public string? BoardId { get; }
    public DriveInfo DriveInfo { get; }

    public Task<List<ITelemetryFile>> GetFiles()
    {
        var files = DriveInfo.RootDirectory.GetFiles("*.SST")
            .TrySelect<FileInfo, ITelemetryFile, FormatException>(f => new MassStorageTelemetryFile(f), null)
            .OrderByDescending(f => f.StartTime)
            .ToList();
        return Task.FromResult(files);
    }

    public MassStorageTelemetryDataStore(DriveInfo driveInfo)
    {
        DriveInfo = driveInfo;
        Name = $"{driveInfo.VolumeLabel} ({DriveInfo.RootDirectory.Name})";
        BoardId = File.ReadAllText($"{DriveInfo.RootDirectory.FullName}/BOARDID").ToLower();

        if (!Directory.Exists($"{DriveInfo.RootDirectory.FullName}/uploaded"))
            Directory.CreateDirectory($"{DriveInfo.RootDirectory.FullName}/uploaded");
    }
}