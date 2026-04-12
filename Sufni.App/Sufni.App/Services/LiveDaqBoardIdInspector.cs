using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Services;

// Sends an IDENTIFY frame over the live protocol and parses the IDENTIFY_ACK
// response to recover the board's 8-byte serial without starting a session.
internal sealed class LiveDaqBoardIdInspector(IBackgroundTaskRunner backgroundTaskRunner) : ILiveDaqBoardIdInspector
{
    public Task<Guid?> InspectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) =>
        backgroundTaskRunner.RunAsync(async () =>
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(address, port, cancellationToken);
            await using var stream = tcp.GetStream();

            var frame = LiveProtocolReader.CreateIdentifyFrame(1);
            await stream.WriteAsync(frame, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var reader = new LiveProtocolReader();
            var buffer = new byte[256];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    throw new IOException("Connection closed before IDENTIFY_ACK was received.");

                reader.Append(buffer.AsSpan(0, read));
                while (reader.TryReadFrame(out var parsed))
                {
                    if (parsed is LiveIdentifyAckFrame ack)
                        return (Guid?)UuidUtil.CreateDeviceUuid(ack.Payload.BoardSerial);
                }
            }
        }, cancellationToken);
}