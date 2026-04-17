using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace Sufni.App.Models;

public class NetworkTelemetryDataStore : ITelemetryDataStore
{
    private static readonly ILogger logger = Log.ForContext<NetworkTelemetryDataStore>();

    private const int BoardIdSize = 8;
    private const int SampleRateSize = 2;
    private const int DirectoryHeaderSize = BoardIdSize + SampleRateSize;
    private const int FileNameSize = 9;
    private const int FileSizeFieldSize = 8;
    private const int TimestampFieldSize = 8;
    private const int DurationFieldSize = 4;
    private const int VersionFieldSize = 1;
    private const int DirectoryEntrySize = FileNameSize + FileSizeFieldSize + TimestampFieldSize + DurationFieldSize + VersionFieldSize;

    public string Name { get; }
    public Guid? BoardId { get; private set; }
    private readonly IPEndPoint ipEndPoint;
    public readonly Task Initialization;

    public async Task<List<ITelemetryFile>> GetFiles()
    {
        var directoryInfo = await SstTcpClient.GetFile(ipEndPoint, 0);
        var listing = ParseDirectoryListing(directoryInfo);
        BoardId = listing.BoardId;

        var files = new List<ITelemetryFile>();
        foreach (var entry in listing.Entries)
        {
            try
            {
                var f = new NetworkTelemetryFile(
                    ipEndPoint,
                    entry.Name,
                    entry.Version,
                    entry.Timestamp,
                    TimeSpan.FromMilliseconds(entry.DurationMilliseconds));
                files.Add(f);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Skipping invalid network file entry {Name}", entry.Name);
            }
        }

        return files.OrderByDescending(f => f.StartTime).ToList();
    }

    internal static NetworkTelemetryDirectoryListing ParseDirectoryListing(byte[] directoryInfo)
    {
        if (directoryInfo.Length < DirectoryHeaderSize)
            throw new FormatException("Network directory listing is truncated.");

        var payloadLength = directoryInfo.Length - DirectoryHeaderSize;
        if (payloadLength % DirectoryEntrySize != 0)
            throw new FormatException("Network directory listing entry size is invalid.");

        using var memoryStream = new MemoryStream(directoryInfo);
        using var reader = new BinaryReader(memoryStream);
        var boardId = reader.ReadBytes(BoardIdSize);
        var parsedBoardId = UuidUtil.CreateDeviceUuid(boardId);
        _ = reader.ReadUInt16();

        var recordCount = payloadLength / DirectoryEntrySize;
        var entries = new List<NetworkTelemetryDirectoryEntry>(recordCount);
        for (var i = 0; i < recordCount; i++)
        {
            var name = Encoding.ASCII.GetString(reader.ReadBytes(FileNameSize));
            var size = reader.ReadUInt64();
            var timestamp = reader.ReadUInt64();
            var durationMilliseconds = reader.ReadUInt32();
            var version = reader.ReadByte();
            entries.Add(new NetworkTelemetryDirectoryEntry(name, size, timestamp, durationMilliseconds, version));
        }

        return new NetworkTelemetryDirectoryListing(parsedBoardId, entries);
    }

    public NetworkTelemetryDataStore(IPAddress address, int port)
    {
        ipEndPoint = new IPEndPoint(address, port);
        Name = $"gosst://{ipEndPoint.Address}:{ipEndPoint.Port}";

        // We need this to set BoardId
        Initialization = GetFiles();
    }
}

internal sealed record NetworkTelemetryDirectoryListing(
    Guid BoardId,
    IReadOnlyList<NetworkTelemetryDirectoryEntry> Entries);

internal readonly record struct NetworkTelemetryDirectoryEntry(
    string Name,
    ulong Size,
    ulong Timestamp,
    uint DurationMilliseconds,
    byte Version);