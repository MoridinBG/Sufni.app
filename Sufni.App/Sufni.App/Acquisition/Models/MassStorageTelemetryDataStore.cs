using System.Reactive.Linq;
using Sufni.App.LiveDaq.Services.LiveStreaming;
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

    public IObservable<DaqTransportHolder?> ObserveTransport() => Observable.Return<DaqTransportHolder?>(null);

    public Task<TelemetryImportBatchAcquisition> OpenImportBatchAsync(Guid batchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<TelemetryImportBatchAcquisition>(new TelemetryImportBatchAcquisition.Acquired(null));

    public string Name => Drive.DisplayName;
    public Guid? BoardId { get; }
    public DaqDrive Drive { get; }

    public Task<TelemetryFileListing> GetFiles(CancellationToken cancellationToken = default) => Task.FromResult<TelemetryFileListing>(
        new TelemetryFileListing.Listed([.. driveProbe.GetFiles(Drive).OrderByDescending(file => file.StartTime)], BoardId));

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
