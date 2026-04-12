using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

// Reads a DAQ's legacy directory listing to recover the persisted board identity
// without materializing a full telemetry datastore instance.
internal sealed class LiveDaqBoardIdInspector(IBackgroundTaskRunner backgroundTaskRunner) : ILiveDaqBoardIdInspector
{
    // Inspects the live endpoint off the UI thread and returns the parsed BOARDID when
    // the endpoint responds with a recognizable directory listing.
    public Task<Guid?> InspectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(async () =>
        {
            var endPoint = new IPEndPoint(address, port);
            var directoryInfo = await SstTcpClient.GetFile(endPoint, 0);
            var listing = NetworkTelemetryDataStore.ParseDirectoryListing(directoryInfo);
            return (Guid?)listing.BoardId;
        }, cancellationToken);
}