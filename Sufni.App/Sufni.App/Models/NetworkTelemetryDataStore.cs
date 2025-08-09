using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Sufni.App.Models;

public class NetworkTelemetryDataStore : ITelemetryDataStore
{
    public string Name { get; }
    public Guid? BoardId { get; private set; }
    private readonly IPEndPoint ipEndPoint;
    public readonly Task Initialization;

    public async Task<List<ITelemetryFile>> GetFiles()
    {
        var directoryInfo = await SstTcpClient.GetFile(ipEndPoint, 0);
        var recordCount = directoryInfo.Length / 25; // 9 characters + size (long) + timestamp (long)
        using var memoryStream = new MemoryStream(directoryInfo);
        using var reader = new BinaryReader(memoryStream);
        var boardId = reader.ReadBytes(8);
        BoardId = UuidUtil.CreateDeviceUuid(boardId);
        var sampleRate = reader.ReadUInt16();

        var files = new List<ITelemetryFile>();
        for (var i = 0; i < recordCount; i++)
        {
            var name = Encoding.ASCII.GetString(reader.ReadBytes(9));
            var size = reader.ReadUInt64();
            var timestamp = reader.ReadUInt64();

            try
            {
                var f = new NetworkTelemetryFile(ipEndPoint, sampleRate, name, size, timestamp);
                files.Add(f);
            }
            catch (Exception)
            {
                // we don't care, the invalid file just simply won't show up in the list
            }
        }

        return files.OrderByDescending(f => f.StartTime).ToList();
    }

    public NetworkTelemetryDataStore(IPAddress address, int port)
    {
        ipEndPoint = new IPEndPoint(address, port);
        Name = $"gosst://{ipEndPoint.Address}:{ipEndPoint.Port}";

        // We need this to set BoardId
        Initialization = GetFiles();
    }
}