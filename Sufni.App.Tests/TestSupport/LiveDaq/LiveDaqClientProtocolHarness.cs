using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal sealed class LiveDaqClientProtocolHarness<TClient>
    where TClient : ILiveDaqClient
{
    private readonly Func<TClient> createClient;

    public LiveDaqClientProtocolHarness(Func<TClient> createClient)
    {
        this.createClient = createClient;
    }

    public async Task<TResult> WithServerAsync<TResult>(
        Func<NetworkStream, Task> serverScript,
        Func<TClient, int, Task<TResult>> clientScript)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            await serverScript(stream);
        });

        await using var client = createClient();
        try
        {
            var result = await clientScript(client, port);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            return result;
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync();
            }
        }
    }

    public static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead));
            if (read == 0)
            {
                throw new EndOfStreamException($"Expected {length} bytes but stream closed after {totalRead}.");
            }

            totalRead += read;
        }

        return buffer;
    }

    public static async Task<IReadOnlyList<LiveProtocolFrame>> ObserveFramesAsync(
        ILiveDaqClient client,
        int expectedCount,
        Func<Task> action)
    {
        var observedFrames = new List<LiveProtocolFrame>();
        var framesObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is not LiveDaqClientEvent.FrameReceived frameReceived)
            {
                return;
            }

            lock (observedFrames)
            {
                observedFrames.Add(frameReceived.Frame);
                if (observedFrames.Count >= expectedCount)
                {
                    framesObserved.TrySetResult();
                }
            }
        });

        await action();
        await framesObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        return observedFrames;
    }

    public static async Task<LiveDaqClientDropCounters> ObserveDropCountersAsync(
        ILiveDaqClient client,
        Func<LiveDaqClientDropCounters, bool> predicate,
        Func<Task> action)
    {
        var observed = new TaskCompletionSource<LiveDaqClientDropCounters>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Events.Subscribe(clientEvent =>
        {
            if (clientEvent is LiveDaqClientEvent.DropCountersChanged countersChanged &&
                predicate(countersChanged.Counters))
            {
                observed.TrySetResult(countersChanged.Counters);
            }
        });

        await action();
        return await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
