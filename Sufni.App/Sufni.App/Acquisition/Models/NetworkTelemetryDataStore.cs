using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

using Sufni.App.Acquisition.Services;
using Sufni.App.LiveDaq.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;

namespace Sufni.App.Acquisition.Models;

public class NetworkTelemetryDataStore : ITelemetryDataStore
{
    public string Name { get; }
    public Guid? BoardId { get; private set; }
    private readonly ServiceEndpoint endpoint;
    private readonly IDaqManagementService daqManagementService;
    private readonly ILiveDaqBoardIdInspector liveDaqBoardIdInspector;
    public readonly Task<BoardIdInspection> Initialization;
    private ImportBatch? activeBatch;

    public Task<IAsyncDisposable?> OpenImportBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        activeBatch = new ImportBatch(this);
        return Task.FromResult<IAsyncDisposable?>(activeBatch);
    }

    private Task<IDaqManagementSession> GetSessionAsync(CancellationToken cancellationToken) =>
        activeBatch?.GetSessionAsync(cancellationToken)
        ?? throw new InvalidOperationException("File operations require an open import batch.");

    private sealed class ImportBatch(NetworkTelemetryDataStore owner) : IAsyncDisposable
    {
        private IDaqManagementSession? session;

        public async Task<IDaqManagementSession> GetSessionAsync(CancellationToken cancellationToken)
        {
            if (session is null)
                session = await owner.daqManagementService.OpenSessionAsync(owner.endpoint, cancellationToken);
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            try { if (session is not null) await session.DisposeAsync(); }
            finally { owner.activeBatch = null; }
        }
    }

    public async Task<List<ITelemetryFile>> GetFiles()
    {
        var directoryResult = await daqManagementService.ListDirectoryAsync(endpoint);

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

        return files.OrderByDescending(f => f.StartTime).ToList();
    }

    private async Task<BoardIdInspection> InitializeAsync()
    {
        var result = await liveDaqBoardIdInspector.InspectAsync(endpoint);
        if (result is BoardIdInspection.Found found) BoardId = found.BoardId;
        return result;
    }

    public NetworkTelemetryDataStore(
        ServiceEndpoint endpoint,
        IDaqManagementService daqManagementService,
        ILiveDaqBoardIdInspector liveDaqBoardIdInspector)
    {
        this.endpoint = endpoint;
        this.daqManagementService = daqManagementService;
        this.liveDaqBoardIdInspector = liveDaqBoardIdInspector;
        Name = $"gosst://{endpoint}";

        Initialization = InitializeAsync();
    }
}
