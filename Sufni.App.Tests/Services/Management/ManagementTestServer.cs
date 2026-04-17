using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services.Management;

namespace Sufni.App.Tests.Services.Management;

internal sealed class ManagementTestServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    public ManagementTestServer()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public string Host => IPAddress.Loopback.ToString();

    public int Port { get; }

    public Task RunSessionAsync(Func<NetworkStream, Task> sessionHandler) => Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        await sessionHandler(stream);
    });

    public static async Task<ManagementProtocolFrame> ReadFrameAsync(
        NetworkStream stream,
        ManagementProtocolReader? reader = null,
        CancellationToken cancellationToken = default)
    {
        reader ??= new ManagementProtocolReader();
        if (reader.TryReadFrame(out var bufferedFrame) && bufferedFrame is not null)
        {
            return bufferedFrame;
        }

        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                throw new IOException("Connection closed before a management frame was received by the test server.");
            }

            reader.Append(buffer.AsSpan(0, read));
            if (reader.TryReadFrame(out var frame) && frame is not null)
            {
                return frame;
            }
        }
    }

    public static async Task WriteFrameAsync(
        NetworkStream stream,
        byte[] frame,
        CancellationToken cancellationToken = default)
    {
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        listener.Stop();
        return ValueTask.CompletedTask;
    }
}