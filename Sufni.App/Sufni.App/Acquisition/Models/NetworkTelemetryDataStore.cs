using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services;
using Sufni.App.LiveDaq.Services;
using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Acquisition.Models;

public class NetworkTelemetryDataStore : ITelemetryDataStore
{
    public string Name { get; }
    public Guid? BoardId { get; private set; }
    private readonly IPEndPoint ipEndPoint;
    private readonly IDaqManagementService daqManagementService;
    private readonly ILiveDaqBoardIdInspector liveDaqBoardIdInspector;
    public readonly Task<BoardIdInspection> Initialization;

    public async Task<List<ITelemetryFile>> GetFiles()
    {
        var directoryResult = await daqManagementService.ListDirectoryAsync(
            ipEndPoint.Address.ToString(),
            ipEndPoint.Port);

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
                        ipEndPoint,
                        malformedRecord.RecordId,
                        malformedRecord.Name,
                        malformedRecord.TimestampUtc,
                        malformedRecord.Duration,
                        malformedRecord.MalformedMessage,
                        canImport: false));
                    break;
                case DaqSstFileRecord sstRecord:
                    files.Add(new NetworkTelemetryFile(
                        ipEndPoint,
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
        var result = await liveDaqBoardIdInspector.InspectAsync(ipEndPoint.Address, ipEndPoint.Port);
        if (result is BoardIdInspection.Found found) BoardId = found.BoardId;
        return result;
    }

    public NetworkTelemetryDataStore(
        IPAddress address,
        int port,
        IDaqManagementService daqManagementService,
        ILiveDaqBoardIdInspector liveDaqBoardIdInspector)
    {
        ipEndPoint = new IPEndPoint(address, port);
        this.daqManagementService = daqManagementService;
        this.liveDaqBoardIdInspector = liveDaqBoardIdInspector;
        Name = $"gosst://{ipEndPoint.Address}:{ipEndPoint.Port}";

        Initialization = InitializeAsync();
    }
}
