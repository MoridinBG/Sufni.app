using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Acquisition.Services;

namespace Sufni.App.Acquisition.Models;

public sealed class MassStorageTelemetryDataStore : ITelemetryDataStore
{
    private readonly IDaqDriveProbe driveProbe;

    public Task<IAsyncDisposable?> OpenImportBatchAsync(Guid batchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncDisposable?>(null);

    public string Name => Drive.DisplayName;
    public Guid? BoardId { get; }
    public DaqDrive Drive { get; }

    public Task<List<ITelemetryFile>> GetFiles() => Task.FromResult(
        driveProbe.GetFiles(Drive).OrderByDescending(file => file.StartTime).ToList());

    public static async Task<MassStorageTelemetryDataStore> CreateAsync(
        DaqDrive drive,
        IDaqDriveProbe driveProbe,
        CancellationToken cancellationToken = default) =>
        new(drive, await driveProbe.InitializeAsync(drive, cancellationToken), driveProbe);

    private MassStorageTelemetryDataStore(DaqDrive drive, Guid boardId, IDaqDriveProbe driveProbe)
    {
        Drive = drive;
        BoardId = boardId;
        this.driveProbe = driveProbe;
    }
}
