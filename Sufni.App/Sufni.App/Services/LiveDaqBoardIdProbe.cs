using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

internal sealed class LiveDaqBoardIdProbe(IBackgroundTaskRunner backgroundTaskRunner) : ILiveDaqBoardIdProbe
{
    public Task<Guid?> ProbeAsync(IPAddress address, int port, CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(async () =>
        {
            var endPoint = new IPEndPoint(address, port);
            var directoryInfo = await SstTcpClient.GetFile(endPoint, 0);
            var listing = NetworkTelemetryDataStore.ParseDirectoryListing(directoryInfo);
            return (Guid?)listing.BoardId;
        }, cancellationToken);
}