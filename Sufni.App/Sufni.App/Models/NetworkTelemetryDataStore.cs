using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Sufni.App.Services;
using Sufni.App.Services.Management;

namespace Sufni.App.Models;

public class NetworkTelemetryDataStore : ITelemetryDataStore
{
    public string Name { get; }
    public Guid? BoardId { get; private set; }
    private readonly IPEndPoint ipEndPoint;
    private readonly IDaqManagementService daqManagementService;
    private readonly ILiveDaqBoardIdInspector liveDaqBoardIdInspector;
    public readonly Task Initialization;

    public async Task<List<ITelemetryFile>> GetFiles()
    {
        var directoryResult = await daqManagementService.ListDirectoryAsync(
            ipEndPoint.Address.ToString(),
            ipEndPoint.Port,
            DaqDirectoryId.Root);

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
                        daqManagementService,
                        malformedRecord.RecordId,
                        malformedRecord.Name,
                        malformedRecord.SstVersion,
                        malformedRecord.TimestampUtc,
                        malformedRecord.Duration,
                        malformedRecord.MalformedMessage));
                    break;
                case DaqSstFileRecord sstRecord:
                    files.Add(new NetworkTelemetryFile(
                        ipEndPoint,
                        daqManagementService,
                        sstRecord.RecordId,
                        sstRecord.Name,
                        sstRecord.SstVersion,
                        sstRecord.TimestampUtc,
                        sstRecord.Duration));
                    break;
            }
        }

        return files.OrderByDescending(f => f.StartTime).ToList();
    }

    private async Task InitializeAsync()
    {
        BoardId = await liveDaqBoardIdInspector.InspectAsync(ipEndPoint.Address, ipEndPoint.Port);
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