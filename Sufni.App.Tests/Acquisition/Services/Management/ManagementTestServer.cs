using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.Tests.Acquisition.Services.Management;

internal sealed class ManagementTestServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly object gate = new();
    private readonly List<Task> sessions = [];
    private readonly HashSet<TcpClient> clients = [];
    private bool disposed;

    public ManagementTestServer()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public string Host => IPAddress.Loopback.ToString();

    public int Port { get; }

    public Task RunSessionAsync(Func<NetworkStream, Task> sessionHandler)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var cancellationToken = lifetime.Token;
            var session = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                lock (gate)
                {
                    if (disposed) throw new OperationCanceledException(cancellationToken);
                    clients.Add(client);
                }
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using var stream = client.GetStream();
                    await sessionHandler(stream);
                }
                finally
                {
                    lock (gate) clients.Remove(client);
                }
            });
            sessions.Add(session);
            return session;
        }
    }

    public static async Task<ManagementProtocolFrame> ReadFrameAsync(
        NetworkStream stream,
        ManagementRequestReader? reader = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken == default) cancellationToken = TestContext.Current.CancellationToken;
        reader ??= new ManagementRequestReader();
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
        if (cancellationToken == default) cancellationToken = TestContext.Current.CancellationToken;
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] startedSessions;
        TcpClient[] acceptedClients;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            startedSessions = sessions.ToArray();
            acceptedClients = clients.ToArray();
        }
        lifetime.Cancel();
        listener.Stop();
        foreach (var client in acceptedClients) client.Dispose();
        var completion = Task.WhenAll(startedSessions);
        // Returned session tasks carry operation failures; cleanup must preserve a client's assertion.
        _ = completion.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { await completion.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { }
        finally { lifetime.Dispose(); }
    }
}
