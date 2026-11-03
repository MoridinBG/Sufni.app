using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Serilog;

using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Shared.Common;
namespace Sufni.App.Acquisition.Services;

// Sends an IDENTIFY frame over the live protocol and parses the IDENTIFY_ACK
// response to recover the board's 8-byte serial without starting a session.
internal sealed class LiveDaqBoardIdInspector : ILiveDaqBoardIdInspector
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqBoardIdInspector>();

    // Caps a single inspection so an accepting-but-unresponsive socket cannot
    // leak tasks indefinitely when mDNS advertisements flap.
    private static readonly TimeSpan DefaultInspectionTimeout = TimeSpan.FromSeconds(5);

    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly TimeProvider timeProvider;

    public LiveDaqBoardIdInspector(IBackgroundTaskRunner backgroundTaskRunner, TimeProvider timeProvider)
    {
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.timeProvider = timeProvider;
    }

    public Task<BoardIdInspection> InspectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync<BoardIdInspection>(async () =>
        {
            using var deadline = new CancellationTokenSource(DefaultInspectionTimeout, timeProvider);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            var token = linkedCts.Token;

            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(endpoint.GetAddresses(), endpoint.Port, token);
                await using var stream = tcp.GetStream();

                var frame = LiveV2ProtocolReader.CreateIdentifyFrame(1);
                await stream.WriteAsync(frame, token);
                await stream.FlushAsync(token);

                var reader = new LiveV2ProtocolReader();
                var buffer = new byte[256];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, token);
                    if (read == 0)
                        throw new IOException("Connection closed before IDENTIFY_ACK was received.");

                    reader.Append(buffer.AsSpan(0, read));
                    while (reader.TryReadFrame(out var parsed))
                    {
                        if (parsed is LiveIdentifyAckFrame ack)
                            return new BoardIdInspection.Found(UuidUtil.CreateDeviceUuid(ack.Payload.BoardSerial));
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new BoardIdInspection.TimedOut();
            }
            catch (Exception e) when (e is SocketException or IOException or FormatException)
            {
                logger.Warning(e, "Board identity unavailable at {Endpoint}", endpoint);
                return new BoardIdInspection.Unreachable();
            }
        }, cancellationToken);
}
