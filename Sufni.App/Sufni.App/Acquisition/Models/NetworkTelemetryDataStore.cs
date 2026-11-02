using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

using Sufni.App.Acquisition.Services;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;

namespace Sufni.App.Acquisition.Models;

public class NetworkTelemetryDataStore : ITelemetryDataStore
{
    internal LiveDaqCatalogEntry CatalogEntry { get; }
    public string Name => $"gosst://{CatalogEntry.NetworkEndpoint}";
    public Guid? BoardId => Guid.TryParse(CatalogEntry.BoardId, out var id) ? id : null;
    private readonly ILiveDaqCatalogService catalog;
    private readonly ILiveDaqSharedStreamRegistry registry;
    private readonly IDaqManagementService daqManagementService;
    private readonly ILiveDaqBoardIdInspector liveDaqBoardIdInspector;
    private ImportBatch? activeBatch;

    public IObservable<DaqTransportHolder?> ObserveTransport() => registry.ObserveTransport(CatalogEntry.IdentityKey);

    public async Task<TelemetryImportBatchAcquisition> OpenImportBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = catalog.Get(CatalogEntry.IdentityKey);
        if (current is null || !current.SupportsManagement) return new TelemetryImportBatchAcquisition.Unavailable();
        var acquisition = await registry.AcquireImportAsync(current.IdentityKey, batchId);
        if (acquisition is DaqTransportAcquisition.Busy busy) return new TelemetryImportBatchAcquisition.Busy(busy.Holder);
        activeBatch = new ImportBatch(this, current.NetworkEndpoint, ((DaqTransportAcquisition.Acquired)acquisition).Lease);
        return new TelemetryImportBatchAcquisition.Acquired(activeBatch);
    }

    private Task<IDaqManagementSession> GetSessionAsync(CancellationToken cancellationToken) =>
        activeBatch?.GetSessionAsync(cancellationToken)
        ?? throw new InvalidOperationException("File operations require an open import batch.");

    private sealed class ImportBatch(NetworkTelemetryDataStore owner, ServiceEndpoint endpoint, IDisposable reservation) : IAsyncDisposable
    {
        private IDaqManagementSession? session;

        public async Task<IDaqManagementSession> GetSessionAsync(CancellationToken cancellationToken)
        {
            if (session is null)
                session = await owner.daqManagementService.OpenSessionAsync(endpoint, cancellationToken);
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            try { if (session is not null) await session.DisposeAsync(); }
            finally
            {
                owner.activeBatch = null;
                reservation.Dispose();
            }
        }
    }

    public async Task<TelemetryFileListing> GetFiles(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = catalog.Get(CatalogEntry.IdentityKey);
        if (current is null || !current.SupportsManagement) return new TelemetryFileListing.Unavailable();
        Guid? boardId = Guid.TryParse(current.BoardId, out var knownId) ? knownId : null;
        if (boardId is null)
        {
            var identify = await registry.AcquireIdentifyAsync(current.IdentityKey);
            if (identify is DaqTransportAcquisition.Busy identifyingBusy) return new TelemetryFileListing.Busy(identifyingBusy.Holder);
            using var reservation = ((DaqTransportAcquisition.Acquired)identify).Lease;
            if (await liveDaqBoardIdInspector.InspectAsync(current.NetworkEndpoint, cancellationToken) is not BoardIdInspection.Found found)
                return new TelemetryFileListing.Unavailable();
            boardId = found.BoardId;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (catalog.Get(current.IdentityKey) != current) return new TelemetryFileListing.Unavailable();
        var acquisition = await registry.AcquireManagementAsync(current.IdentityKey, DaqManagementOperation.ListFiles);
        if (acquisition is DaqTransportAcquisition.Busy busy) return new TelemetryFileListing.Busy(busy.Holder);
        using var listingReservation = ((DaqTransportAcquisition.Acquired)acquisition).Lease;
        var directoryResult = await daqManagementService.ListDirectoryAsync(current.NetworkEndpoint, cancellationToken);

        var directory = directoryResult switch
        {
            DaqListDirectoryResult.Listed listed => listed.Directory as DaqRootDirectoryRecord
                ?? throw new DaqManagementException("ROOT listing did not return a root directory record."),
            DaqListDirectoryResult.Error error => throw new DaqManagementException(error.ErrorCode, error.Message),
            _ => throw new DaqManagementException("LIST_DIR returned an unsupported result shape.")
        };

        var files = new List<ITelemetryFile>();
        foreach (var record in directory.Files)
        {
            switch (record)
            {
                case DaqConfigFileRecord:
                    continue;
                case DaqMalformedSstFileRecord malformedRecord:
                    files.Add(new NetworkTelemetryFile(
                        GetSessionAsync,
                        malformedRecord.RecordId,
                        malformedRecord.Name,
                        malformedRecord.TimestampUtc,
                        malformedRecord.Duration,
                        malformedRecord.MalformedMessage,
                        canImport: false));
                    break;
                case DaqSstFileRecord sstRecord:
                    files.Add(new NetworkTelemetryFile(
                        GetSessionAsync,
                        sstRecord.RecordId,
                        sstRecord.Name,
                        sstRecord.TimestampUtc,
                        sstRecord.Duration));
                    break;
            }
        }

        return new TelemetryFileListing.Listed([.. files.OrderByDescending(f => f.StartTime)], boardId);
    }

    public NetworkTelemetryDataStore(
        LiveDaqCatalogEntry entry,
        ILiveDaqCatalogService catalog,
        ILiveDaqSharedStreamRegistry registry,
        IDaqManagementService daqManagementService,
        ILiveDaqBoardIdInspector liveDaqBoardIdInspector)
    {
        CatalogEntry = entry;
        this.catalog = catalog;
        this.registry = registry;
        this.daqManagementService = daqManagementService;
        this.liveDaqBoardIdInspector = liveDaqBoardIdInspector;
    }
}
