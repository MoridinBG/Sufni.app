using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;

namespace Sufni.App.Tests.TestSupport.Acquisition;

public sealed class FakeMassStorageDriveSource : IDaqDriveProbe
{
    public List<DaqDrive> Drives { get; } = [];
    public Dictionary<string, Guid> BoardIds { get; } = [];
    public Dictionary<string, IReadOnlyList<ITelemetryFile>> Files { get; } = [];

    public IReadOnlyList<DaqDrive> FindDrives() => Drives.ToArray();

    public Task<Guid> InitializeAsync(DaqDrive drive, CancellationToken cancellationToken) =>
        Task.FromResult(BoardIds[drive.RootPath]);

    public IReadOnlyList<ITelemetryFile> GetFiles(DaqDrive drive) =>
        Files.GetValueOrDefault(drive.RootPath, []);
}
